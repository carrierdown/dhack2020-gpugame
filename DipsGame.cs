using System;
using System.IO;
using System.Numerics;
using System.Text;
using DinoDips.AssetPrimitives;
using Veldrid;
using Veldrid.SPIRV;

namespace DinoDips
{
    public class DipsGame : SampleApplication
    {
        private readonly VertexPositionTexture[] _vertices;
        private readonly ushort[] _indices;
        private DeviceBuffer _projectionBuffer;
        private DeviceBuffer _viewBuffer;
        private DeviceBuffer _worldBuffer;
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private CommandList _cl;
        private Pipeline _pipeline;
        private ResourceSet _projViewSet;
        private ResourceSet _mainAvatarTextureSet;
        private const float _startingPlayerVelocity = -24f;
        private const float _energyLoss = 0.78f;
        private const int _numJumpCycles = 62;
        private const int _speedFactor = 6;
        private long _playerJumping = -1;
        private int[] _cactusPlacements = {1500, 2000, 2700, 3400};
        private uint[] _cactusTypes = {0, 1, 0, 0, 2, 0, 1, 2, 0, 1, 0, 2, 0, 0};
        private uint[] _indexStartMap = {6, 18, 24};
        private const int _playerX = 50;
        private long _prevTicks = 0;

        private bool death = false;
        private int curDeathWaitTicks = 0;
        private const int deathWaitTicks = 40;

        private float _curPlayerVertPos = 320;
        private float _playerVelocity = _startingPlayerVelocity;

        public DipsGame(IApplicationWindow window) : base(window)
        {
            _vertices = GetCubeVertices();
            _indices = GetCubeIndices();
        }

        protected override void OnKeyDown(KeyEvent keyEvent)
        {
            if (keyEvent.Key == Key.Up)
            {
                if (_playerJumping < 0)
                    _playerJumping = 0;
            }
        }

        private Texture GetGraphicsDataAsDeviceTexture(string path)
        {
            using var reader = new BinaryReader(File.Open(path, FileMode.Open));
            var texture = new ProcessedTexture(
                reader.ReadEnum<PixelFormat>(),
                reader.ReadEnum<TextureType>(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadByteArray());
            return texture.CreateDeviceTexture(GraphicsDevice, ResourceFactory, TextureUsage.Sampled);
        }

        protected override void CreateResources(ResourceFactory factory)
        {
            _projectionBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _viewBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            _vertexBuffer =
                factory.CreateBuffer(new BufferDescription(
                    (uint) (VertexPositionTexture.SizeInBytes * _vertices.Length), BufferUsage.VertexBuffer));
            GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, _vertices);

            _indexBuffer =
                factory.CreateBuffer(new BufferDescription(sizeof(ushort) * (uint) _indices.Length,
                    BufferUsage.IndexBuffer));
            GraphicsDevice.UpdateBuffer(_indexBuffer, 0, _indices);
            Console.WriteLine(AppDomain.CurrentDomain.BaseDirectory);
            
            var mainAvatarTextureView =
                factory.CreateTextureView(
                    GetGraphicsDataAsDeviceTexture(Path.Join(AppDomain.CurrentDomain.BaseDirectory, @"assets\spritesheet.binary")));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate,
                            VertexElementFormat.Float3),
                        new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate,
                            VertexElementFormat.Float2))
                },
                factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexCode), "main"),
                    new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentCode), "main")));

            ResourceLayout projViewLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer,
                        ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
                ));

            ResourceLayout worldTextureLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer,
                        ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly,
                        ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler,
                        ShaderStages.Fragment)));

            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleList,
                shaderSet,
                new[] {projViewLayout, worldTextureLayout},
                MainSwapchain.Framebuffer.OutputDescription));

            _projViewSet = factory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                _projectionBuffer,
                _viewBuffer));

            _mainAvatarTextureSet = factory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                _worldBuffer,
                mainAvatarTextureView,
                GraphicsDevice.Aniso4xSampler));

            _cl = factory.CreateCommandList();
        }

        protected override void Draw(float deltaSeconds, long ticks)
        {
            if (death && curDeathWaitTicks == deathWaitTicks)
            {
                _prevTicks = ticks;
            }

            if (death)
            {
                curDeathWaitTicks--;
                if (curDeathWaitTicks <= 0)
                {
                    curDeathWaitTicks = -1;
                    death = false;
                    _prevTicks = ticks;
                    ticks = 0;
                }
                else
                {
                    ticks = _prevTicks;
                }
            }
            else
            {
                ticks -= _prevTicks;
            }

            _cl.Begin();

            _cl.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            _cl.UpdateBuffer(_projectionBuffer, 0, Matrix4x4.CreateOrthographicOffCenter(
                0,
                GraphicsDevice.SwapchainFramebuffer.Width,
                GraphicsDevice.SwapchainFramebuffer.Height,
                0,
                -1,
                1
            ));
            
            _cl.UpdateBuffer(_viewBuffer, 0, Matrix4x4.CreateTranslation(Vector3.Zero));

            if (_playerJumping >= 0)
            {
                _curPlayerVertPos += _playerVelocity;
                _playerVelocity += _energyLoss;
                _playerJumping++;
            }

            if (_playerJumping > _numJumpCycles)
            {
                _curPlayerVertPos = 320;
                _playerJumping = -1;
                _playerVelocity = _startingPlayerVelocity;
            }

            _cl.UpdateBuffer(_worldBuffer, 0, Matrix4x4.CreateTranslation(new Vector3(_playerX, _curPlayerVertPos, 0)));

            _cl.ClearColorTarget(0, RgbaFloat.White);
            _cl.ClearDepthStencil(1f);
            _cl.SetPipeline(_pipeline);
            _cl.SetVertexBuffer(0, _vertexBuffer);
            _cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            _cl.SetGraphicsResourceSet(0, _projViewSet);
            _cl.SetGraphicsResourceSet(1, _mainAvatarTextureSet);
            _cl.DrawIndexed(6, 1, 0, 0, 0);
            var xCoord = (0 - (ticks * _speedFactor) % 2000);
            _cl.UpdateBuffer(_worldBuffer, 0, Matrix4x4.CreateTranslation(new Vector3(xCoord, 150, 0)));
            _cl.DrawIndexed(6, 1, 12, 0, 0);
            xCoord = 2000 - ((ticks * _speedFactor) % 2000);
            _cl.UpdateBuffer(_worldBuffer, 0, Matrix4x4.CreateTranslation(new Vector3(xCoord, 150, 0)));
            _cl.DrawIndexed(6, 1, 12, 0, 0);

            for (var i = 0; i < _cactusPlacements.Length; i++)
            {
                var cactusPlacement = _cactusPlacements[i];
                var actualCactusPlacement = cactusPlacement - ((ticks * _speedFactor) % 3650);
                if (actualCactusPlacement > 0 && actualCactusPlacement < 190 && _curPlayerVertPos > 200 && !death)
                {
                    death = true;
                    curDeathWaitTicks = deathWaitTicks;
                    break;
                }

                _cl.UpdateBuffer(_worldBuffer, 0,
                    Matrix4x4.CreateTranslation(new Vector3(actualCactusPlacement, 300, 0)));
                _cl.DrawIndexed(6, 1, _indexStartMap[_cactusTypes[i % _cactusTypes.Length]], 0, 0);
            }

            _cl.End();
            if (!death) GraphicsDevice.SubmitCommands(_cl);
            GraphicsDevice.SwapBuffers();
            GraphicsDevice.WaitForIdle();
        }

        private static VertexPositionTexture[] GetCubeVertices()
        {
            VertexPositionTexture[] vertices = new[]
            {
                new VertexPositionTexture(new Vector3(0, 0, 0), new Vector2(0, 0)),
                new VertexPositionTexture(new Vector3(200, 0, 0), new Vector2(.1f, 0)),
                new VertexPositionTexture(new Vector3(200, 300, 0), new Vector2(.1f, .5f)),
                new VertexPositionTexture(new Vector3(0, 300, 0), new Vector2(0, .5f)),
                new VertexPositionTexture(new Vector3(0, 0, 0), new Vector2(.1f, 0)),
                new VertexPositionTexture(new Vector3(200, 0, 0), new Vector2(.2f, 0)),
                new VertexPositionTexture(new Vector3(200, 300, 0), new Vector2(.2f, .5f)),
                new VertexPositionTexture(new Vector3(0, 300, 0), new Vector2(.1f, .5f)),
                new VertexPositionTexture(new Vector3(0, 300, 0), new Vector2(0, .5f)),
                new VertexPositionTexture(new Vector3(2000, 300, 0), new Vector2(1, .5f)),
                new VertexPositionTexture(new Vector3(2000, 600, 0), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3(0, 600, 0), new Vector2(0, 1)),

                new VertexPositionTexture(new Vector3(0, 0, 0), new Vector2(.2f, 0)),
                new VertexPositionTexture(new Vector3(200, 0, 0), new Vector2(.3f, 0)),
                new VertexPositionTexture(new Vector3(200, 300, 0), new Vector2(.3f, .5f)),
                new VertexPositionTexture(new Vector3(0, 300, 0), new Vector2(.2f, .5f)),

                new VertexPositionTexture(new Vector3(0, 0, 0), new Vector2(.3f, 0)),
                new VertexPositionTexture(new Vector3(200, 0, 0), new Vector2(.4f, 0)),
                new VertexPositionTexture(new Vector3(200, 300, 0), new Vector2(.4f, .5f)),
                new VertexPositionTexture(new Vector3(0, 300, 0), new Vector2(.3f, .5f)),
            };
            return vertices;
        }

        private static ushort[] GetCubeIndices()
        {
            ushort[] indices =
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 21,
            };

            return indices;
        }

        private const string VertexCode = @"
#version 450

layout(set = 0, binding = 0) uniform ProjectionBuffer
{
    mat4 Projection;
};

layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
};

layout(set = 1, binding = 0) uniform WorldBuffer
{
    mat4 World;
};

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec2 fsin_texCoords;

void main()
{
    vec4 worldPosition = World * vec4(Position, 1);
    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;
    gl_Position = clipPosition;
    fsin_texCoords = TexCoords;
}";

        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec2 fsin_texCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform texture2D SurfaceTexture;
layout(set = 1, binding = 2) uniform sampler SurfaceSampler;

void main()
{
    fsout_color =  texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_texCoords);
}";
    }

    public struct VertexPositionTexture
    {
        public const uint SizeInBytes = 20;

        public float PosX;
        public float PosY;
        public float PosZ;

        public float TexU;
        public float TexV;

        public VertexPositionTexture(Vector3 pos, Vector2 uv)
        {
            PosX = pos.X;
            PosY = pos.Y;
            PosZ = pos.Z;
            TexU = uv.X;
            TexV = uv.Y;
        }
    }
}