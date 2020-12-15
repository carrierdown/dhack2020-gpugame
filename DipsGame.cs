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
        private readonly VertexPositionTexture[] Vertices;
        private readonly ushort[] Indices;
        private DeviceBuffer ProjectionBuffer;
        private DeviceBuffer ViewBuffer;
        private DeviceBuffer WorldBuffer;
        private DeviceBuffer VertexBuffer;
        private DeviceBuffer IndexBuffer;
        private CommandList CommandList;
        private Pipeline Pipeline;
        private ResourceSet ProjViewSet;
        private ResourceSet MainAvatarTextureSet;
        
        private const float StartingPlayerVelocity = -24f;
        private const float EnergyLoss = 0.78f;
        private const int NumJumpCycles = 62;
        private const int SpeedFactor = 6;
        private long PlayerJumping = -1;
        private readonly int[] CactusPlacements = {1500, 2000, 2700, 3400};
        private readonly uint[] CactusTypes = {0, 1, 0, 0, 2, 0, 1, 2, 0, 1, 0, 2, 0, 0};
        private readonly uint[] IndexStartMap = {6, 18, 24};
        private const int PlayerX = 50;
        private long PrevTicks = 0;

        private bool Death = false;
        private int CurDeathWaitTicks = 0;
        private const int DeathWaitTicks = 40;

        private float CurPlayerVertPos = 320;
        private float PlayerVelocity = StartingPlayerVelocity;

        public DipsGame(IApplicationWindow window) : base(window)
        {
            Vertices = GetSpriteVertices();
            Indices = GetSpriteIndices();
        }

        protected override void OnKeyDown(KeyEvent keyEvent)
        {
            if (keyEvent.Key == Key.Up)
            {
                if (PlayerJumping < 0)
                    PlayerJumping = 0;
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
            ProjectionBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            ViewBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            WorldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            VertexBuffer =
                factory.CreateBuffer(new BufferDescription(
                    (uint) (VertexPositionTexture.SizeInBytes * Vertices.Length), BufferUsage.VertexBuffer));
            GraphicsDevice.UpdateBuffer(VertexBuffer, 0, Vertices);

            IndexBuffer =
                factory.CreateBuffer(new BufferDescription(sizeof(ushort) * (uint) Indices.Length,
                    BufferUsage.IndexBuffer));
            GraphicsDevice.UpdateBuffer(IndexBuffer, 0, Indices);
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

            Pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleList,
                shaderSet,
                new[] {projViewLayout, worldTextureLayout},
                MainSwapchain.Framebuffer.OutputDescription));

            ProjViewSet = factory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                ProjectionBuffer,
                ViewBuffer));

            MainAvatarTextureSet = factory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                WorldBuffer,
                mainAvatarTextureView,
                GraphicsDevice.Aniso4xSampler));

            CommandList = factory.CreateCommandList();
        }

        protected override void Draw(float deltaSeconds, long ticks)
        {
            if (Death && CurDeathWaitTicks == DeathWaitTicks)
            {
                PrevTicks = ticks;
            }

            if (Death)
            {
                CurDeathWaitTicks--;
                if (CurDeathWaitTicks <= 0)
                {
                    CurDeathWaitTicks = -1;
                    Death = false;
                    PrevTicks = ticks;
                    ticks = 0;
                }
                else
                {
                    ticks = PrevTicks;
                }
            }
            else
            {
                ticks -= PrevTicks;
            }

            CommandList.Begin();

            CommandList.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            CommandList.UpdateBuffer(ProjectionBuffer, 0, Matrix4x4.CreateOrthographicOffCenter(
                0,
                GraphicsDevice.SwapchainFramebuffer.Width,
                GraphicsDevice.SwapchainFramebuffer.Height,
                0,
                -1,
                1
            ));
            
            CommandList.UpdateBuffer(ViewBuffer, 0, Matrix4x4.CreateTranslation(Vector3.Zero));

            if (PlayerJumping >= 0)
            {
                CurPlayerVertPos += PlayerVelocity;
                PlayerVelocity += EnergyLoss;
                PlayerJumping++;
            }

            if (PlayerJumping > NumJumpCycles)
            {
                CurPlayerVertPos = 320;
                PlayerJumping = -1;
                PlayerVelocity = StartingPlayerVelocity;
            }

            CommandList.UpdateBuffer(WorldBuffer, 0, Matrix4x4.CreateTranslation(new Vector3(PlayerX, CurPlayerVertPos, 0)));

            CommandList.ClearColorTarget(0, RgbaFloat.White);
            CommandList.ClearDepthStencil(1f);
            CommandList.SetPipeline(Pipeline);
            CommandList.SetVertexBuffer(0, VertexBuffer);
            CommandList.SetIndexBuffer(IndexBuffer, IndexFormat.UInt16);
            CommandList.SetGraphicsResourceSet(0, ProjViewSet);
            CommandList.SetGraphicsResourceSet(1, MainAvatarTextureSet);
            CommandList.DrawIndexed(6, 1, 0, 0, 0);
            var xCoord = (0 - (ticks * SpeedFactor) % 2000);
            CommandList.UpdateBuffer(WorldBuffer, 0, Matrix4x4.CreateTranslation(new Vector3(xCoord, 150, 0)));
            CommandList.DrawIndexed(6, 1, 12, 0, 0);
            xCoord = 2000 - ((ticks * SpeedFactor) % 2000);
            CommandList.UpdateBuffer(WorldBuffer, 0, Matrix4x4.CreateTranslation(new Vector3(xCoord, 150, 0)));
            CommandList.DrawIndexed(6, 1, 12, 0, 0);

            for (var i = 0; i < CactusPlacements.Length; i++)
            {
                var cactusPlacement = CactusPlacements[i];
                var actualCactusPlacement = cactusPlacement - ((ticks * SpeedFactor) % 3650);
                if (actualCactusPlacement > 0 && actualCactusPlacement < 190 && CurPlayerVertPos > 200 && !Death)
                {
                    Death = true;
                    CurDeathWaitTicks = DeathWaitTicks;
                    break;
                }

                CommandList.UpdateBuffer(WorldBuffer, 0,
                    Matrix4x4.CreateTranslation(new Vector3(actualCactusPlacement, 300, 0)));
                CommandList.DrawIndexed(6, 1, IndexStartMap[CactusTypes[i % CactusTypes.Length]], 0, 0);
            }

            CommandList.End();
            if (!Death) GraphicsDevice.SubmitCommands(CommandList);
            GraphicsDevice.SwapBuffers();
            GraphicsDevice.WaitForIdle();
        }

        private static VertexPositionTexture[] GetSpriteVertices()
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

        private static ushort[] GetSpriteIndices()
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