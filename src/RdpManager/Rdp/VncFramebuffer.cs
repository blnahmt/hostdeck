using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MarcusW.VncClient;
using MarcusW.VncClient.Rendering;

using PixelFormat = MarcusW.VncClient.PixelFormat;
using Screen = MarcusW.VncClient.Screen;
using Size = MarcusW.VncClient.Size;

namespace RdpManager.Rdp;

/// <summary>
/// VNC kutuphanesinin cizdigi cerceve tamponu. Kutuphane alim thread'inde bu tampona
/// yazar; her yazimdan sonra UI thread'inde tek bir <see cref="WriteableBitmap"/> guncellenir
/// (birikmis guncellemeler birlestirilir).
/// </summary>
internal sealed class VncFramebuffer : IRenderTarget, IDisposable
{
    // Bellekte B,G,R,X: WPF Bgr32 ile birebir, donusum gerekmez.
    private static readonly PixelFormat Bgr32 = new("BGRX32", 32, 24, false, true, false, 255, 255, 255, 0, 16, 8, 0, 0);

    // Monitor yerine semafor: kutuphane kilidi alip baska thread'de birakabilir.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dispatcher _dispatcher;
    private readonly Action<WriteableBitmap> _bitmapChanged;

    private IntPtr _buffer;
    private int _width, _height;
    private int _presentPending;
    private WriteableBitmap? _bitmap;
    private bool _disposed;

    public VncFramebuffer(Dispatcher dispatcher, Action<WriteableBitmap> bitmapChanged)
    {
        _dispatcher = dispatcher;
        _bitmapChanged = bitmapChanged;
    }

    /// <summary>Uzak ekranin piksel boyutu (henuz cerceve gelmediyse 0).</summary>
    public (int Width, int Height) PixelSize => (_width, _height);

    public IFramebufferReference GrabFramebufferReference(Size size, IImmutableSet<Screen> layout)
    {
        _lock.Wait();
        if (_disposed)
        {
            _lock.Release();
            throw new ObjectDisposedException(nameof(VncFramebuffer));
        }

        if (size.Width != _width || size.Height != _height)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            var bytes = (nint)size.Width * size.Height * 4;
            _buffer = Marshal.AllocHGlobal(bytes);
            unsafe { new Span<byte>((void*)_buffer, checked((int)bytes)).Clear(); }
            _width = size.Width;
            _height = size.Height;
        }

        return new Reference(this, size);
    }

    private void ReleaseAndPresent()
    {
        _lock.Release();
        if (Interlocked.Exchange(ref _presentPending, 1) == 0)
            _dispatcher.BeginInvoke(Present, DispatcherPriority.Render);
    }

    private void Present()
    {
        Interlocked.Exchange(ref _presentPending, 0);
        _lock.Wait();
        try
        {
            if (_disposed || _buffer == IntPtr.Zero) return;

            if (_bitmap is null || _bitmap.PixelWidth != _width || _bitmap.PixelHeight != _height)
            {
                _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgr32, null);
                _bitmapChanged(_bitmap);
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _buffer, _width * _height * 4, _width * 4);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Son kareyi kucuk resim olarak kaydeder.</summary>
    public bool SaveThumbnail(string connectionId)
    {
        _lock.Wait();
        try
        {
            if (_disposed || _buffer == IntPtr.Zero || _width < 2 || _height < 2) return false;
            using var bitmap = new System.Drawing.Bitmap(_width, _height, _width * 4,
                System.Drawing.Imaging.PixelFormat.Format32bppRgb, _buffer);
            return Services.ThumbnailStore.Save(bitmap, connectionId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed class Reference(VncFramebuffer owner, Size size) : IFramebufferReference
    {
        private int _released;

        public IntPtr Address => owner._buffer;
        public Size Size => size;
        public PixelFormat Format => Bgr32;
        public double HorizontalDpi => 96;
        public double VerticalDpi => 96;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) owner.ReleaseAndPresent();
        }
    }
}
