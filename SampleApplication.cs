using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DinoDips.AssetPrimitives;
using Veldrid;

namespace DinoDips
{
    public abstract class SampleApplication
    {
        private readonly Dictionary<Type, BinaryAssetSerializer> _serializers = DefaultSerializers.Get();

        public IApplicationWindow Window { get; }
        public GraphicsDevice GraphicsDevice { get; private set; }
        public ResourceFactory ResourceFactory { get; private set; }
        public Swapchain MainSwapchain { get; private set; }

        public SampleApplication(IApplicationWindow window)
        {
            Window = window;
            Window.Resized += HandleWindowResize;
            Window.GraphicsDeviceCreated += OnGraphicsDeviceCreated;
            Window.GraphicsDeviceDestroyed += OnDeviceDestroyed;
            Window.Rendering += Draw;
            Window.KeyPressed += OnKeyDown;
        }

        public void OnGraphicsDeviceCreated(GraphicsDevice gd, ResourceFactory factory, Swapchain sc)
        {
            GraphicsDevice = gd;
            ResourceFactory = factory;
            MainSwapchain = sc;
            CreateResources(factory);
            CreateSwapchainResources(factory);
        }

        protected virtual void OnDeviceDestroyed()
        {
            GraphicsDevice = null;
            ResourceFactory = null;
            MainSwapchain = null;
        }

        protected virtual string GetTitle() => GetType().Name;

        protected abstract void CreateResources(ResourceFactory factory);

        protected virtual void CreateSwapchainResources(ResourceFactory factory) { }

        protected abstract void Draw(float deltaSeconds, long ticks);

        protected virtual void HandleWindowResize() { }

        protected virtual void OnKeyDown(KeyEvent ke) { }

        public Stream OpenEmbeddedAssetStream(string name) => GetType().Assembly.GetManifestResourceStream(name);

        public Shader LoadShader(ResourceFactory factory, string set, ShaderStages stage, string entryPoint)
        {
            string name = $"{set}-{stage.ToString().ToLower()}.{GetExtension(factory.BackendType)}";
            return factory.CreateShader(new ShaderDescription(stage, ReadEmbeddedAssetBytes(name), entryPoint));
        }

        public byte[] ReadEmbeddedAssetBytes(string name)
        {
            using (Stream stream = OpenEmbeddedAssetStream(name))
            {
                byte[] bytes = new byte[stream.Length];
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    stream.CopyTo(ms);
                    return bytes;
                }
            }
        }

        private static string GetExtension(GraphicsBackend backendType)
        {
			bool isMacOS = RuntimeInformation.OSDescription.Contains("Darwin");

            return (backendType == GraphicsBackend.Direct3D11)
                ? "hlsl.bytes"
                : (backendType == GraphicsBackend.Vulkan)
                    ? "450.glsl.spv"
                    : (backendType == GraphicsBackend.Metal)
					    ? isMacOS ? "metallib" : "ios.metallib"
                        : (backendType == GraphicsBackend.OpenGL)
                            ? "330.glsl"
                            : "300.glsles";
        }

        public T LoadEmbeddedAsset<T>(string name)
        {
            if (!_serializers.TryGetValue(typeof(T), out BinaryAssetSerializer serializer))
            {
                throw new InvalidOperationException("No serializer registered for type " + typeof(T).Name);
            }

            using (Stream stream = GetType().Assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("No embedded asset with the name " + name);
                }

                BinaryReader reader = new BinaryReader(stream);
                return (T)serializer.Read(reader);
            }
        }
    }
}
