using System;
using Veldrid;

namespace DinoDips
{
    public interface IApplicationWindow
    {
        event Action<float, long> Rendering;
        event Action<GraphicsDevice, ResourceFactory, Swapchain> GraphicsDeviceCreated;
        event Action GraphicsDeviceDestroyed;
        event Action Resized;
        event Action<KeyEvent> KeyPressed;

        uint Width { get; }
        uint Height { get; }

        void Run();
    }
}
