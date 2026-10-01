using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace RagdollPet;

public partial class MainWindow : Window
{
    private enum PetState { Idle, Walking, FastWalking, Grooming, Transition, Sleeping, Playing }
    private enum BasePose { Stand, Sit, Lie, Sleep }
    private enum DirectionPose { Left, Left3Q, Center, Right3Q, Right }
    private static readonly DirectionPose[] DirectionPoseOrder =
        [DirectionPose.Left, DirectionPose.Left3Q, DirectionPose.Center, DirectionPose.Right3Q, DirectionPose.Right];

    private static class PoseTuning
    {
        public const int SitFrame = 8;
        public const int SitLookFrame = 12;
        public const int PawReachFrame = 16;
        public const int SniffFrame = 4;
        public const double StandSitFps = 20;
        public const double StandBreathAmplitude = .0018;
        public const double SitBreathAmplitude = .0015;
        public const double SleepBreathAmplitude = .0025;
    }

    private sealed class FaceParameters
    {
        public double MouseX { get; set; }
        public double MouseY { get; set; }
        public double EyeLookX { get; set; }
        public double EyeLookY { get; set; }
        public double HeadLookX { get; set; }
        public double HeadLookY { get; set; }
        public double ChestLookX { get; set; }
        public double BodyLookTargetX { get; set; }
        public double BodyLookX { get; set; }
        public double Blink { get; set; }
        public double EarLRotate { get; set; }
        public double EarRRotate { get; set; }
        public double EarLOffsetY { get; set; }
        public double EarROffsetY { get; set; }
        public double HeadOffsetX { get; set; }
        public double HeadOffsetY { get; set; }
        public double HeadRotate { get; set; }
        public double UpperBodyOffsetX { get; set; }
        public double UpperBodyOffsetY { get; set; }
        public double UpperBodyRotate { get; set; }
    }

    private sealed class FaceRuntimeSettings
    {
        public double BlinkMinSeconds { get; set; } = 3.5;
        public double BlinkMaxSeconds { get; set; } = 8;
        public double DoubleBlinkChance { get; set; } = .15;
        public double BlinkCloseSeconds { get; set; } = .08;
        public double BlinkOpenSeconds { get; set; } = .11;
        public double EyeLookMaxX { get; set; } = 4;
        public double EyeLookMaxY { get; set; } = 3;
        public double EyeSmoothing { get; set; } = .14;
        public double EarMouseMaxDegrees { get; set; } = 3;
        public double EarTwitchMinSeconds { get; set; } = 5;
        public double EarTwitchMaxSeconds { get; set; } = 12;
        public double EarTwitchDegrees { get; set; } = 7;
        public double EarSmoothing { get; set; } = .20;
        public double HeadFollowRatioX { get; set; } = .25;
        public double HeadFollowRatioY { get; set; } = .18;
        public double HeadOffsetMaxX { get; set; } = 3.2;
        public double HeadOffsetMaxY { get; set; } = 1.8;
        public double HeadRotateMaxDegrees { get; set; } = 3;
        public double HeadSmoothing { get; set; } = .10;
        public double HeadActivationThreshold { get; set; } = .30;
        public double BodyActivationThreshold { get; set; } = .65;
        public double ChestOffsetMaxX { get; set; } = 2;
        public double ChestOffsetMaxY { get; set; } = .8;
        public double ChestSkewMaxDegrees { get; set; } = 1.2;
        public double ChestSmoothing { get; set; } = .055;
        public double BodySmoothing { get; set; } = .025;
        public double BodyTurnEnterThreshold { get; set; } = .65;
        public double BodyTurnExitThreshold { get; set; } = .40;
        public int BodyTurnHoldMinMs { get; set; } = 300;
        public int BodyTurnHoldMaxMs { get; set; } = 600;
    }

    private sealed class DirectionPoseManifest
    {
        public int CrossfadeDurationMs { get; set; } = 120;
        public double TransitionHeadLookStrength { get; set; } = .35;
        public double TransitionEarLookStrength { get; set; } = .60;
        public double LookStrengthRecoverySmoothing { get; set; } = .14;
        public Dictionary<string, HeadSafeZone> HeadSafeZones { get; set; } = [];
        public Dictionary<string, DirectionFaceProfile> FaceProfiles { get; set; } = [];
        public DirectionHysteresis Hysteresis { get; set; } = new();
        public Dictionary<string, DirectionPoseResource> Poses { get; set; } = [];
    }

    private sealed class DirectionFaceProfile
    {
        public double PupilLStrength { get; set; } = 1;
        public double PupilRStrength { get; set; } = 1;
        public double HeadLookStrength { get; set; } = 1;
        public double EarLookStrength { get; set; } = 1;
    }

    private sealed class DirectionHysteresis
    {
        public double CenterToThreeQuarter { get; set; } = .45;
        public double ThreeQuarterToSide { get; set; } = .82;
        public double SideToThreeQuarter { get; set; } = .68;
        public double ThreeQuarterToCenter { get; set; } = .30;
        public int[] ThreeQuarterHoldMs { get; set; } = [300, 450];
        public int[] SideHoldMs { get; set; } = [450, 700];
    }

    private sealed class HeadSafeZone
    {
        public double HeadMinX { get; set; } = -10;
        public double HeadMaxX { get; set; } = 10;
        public double HeadMinY { get; set; } = -5;
        public double HeadMaxY { get; set; } = 5;
        public double HeadRotateMin { get; set; } = -7;
        public double HeadRotateMax { get; set; } = 7;
    }

    private sealed class DirectionPoseResource
    {
        public bool Available { get; set; }
        public string? Folder { get; set; }
        public string[] RequiredLayers { get; set; } = [];
    }

    private sealed class DirectionLayerManifest
    {
        public int[] Canvas { get; set; } = [];
        public string SourcePolicy { get; set; } = "";
        public Dictionary<string, DirectionLayerPoseManifest> Poses { get; set; } = [];
    }

    private sealed class DirectionLayerPoseManifest
    {
        public string Folder { get; set; } = "";
        public bool CanvasAlignedLayers { get; set; }
        public int[] HeadPivot { get; set; } = [];
        public int[] UpperBodyPivot { get; set; } = [];
        public int[][] Eyes { get; set; } = [];
        public DirectionLayerEarManifest[] Ears { get; set; } = [];
        public string[] Layers { get; set; } = [];
    }

    private sealed class DirectionLayerEarManifest
    {
        public int[] Pivot { get; set; } = [];
        public int[] Box { get; set; } = [];
    }

    private sealed class DirectionTransitionManifest
    {
        public int Version { get; set; }
        public string Easing { get; set; } = "smoothstep";
        public Dictionary<string, DirectionTransitionClipManifest> Clips { get; set; } = [];
    }

    private sealed class DirectionTransitionClipManifest
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
        public int DurationMs { get; set; } = 320;
        public DirectionBridgeFrameManifest[] BridgeFrames { get; set; } = [];
        public DirectionTransitionLayerTiming LayerTiming { get; set; } = new();
    }

    private sealed class DirectionBridgeFrameManifest
    {
        public string File { get; set; } = "";
        public double At { get; set; }
        public string Placeholder { get; set; } = "source";
    }

    private sealed class DirectionTransitionLayerTiming
    {
        public double[] Eye { get; set; } = [0, .34];
        public double[] Head { get; set; } = [.10, .62];
        public double[] Ear { get; set; } = [.08, .78];
        public double[] UpperBody { get; set; } = [.24, .84];
        public double[] Chest { get; set; } = [.30, .90];
        public double[] Body { get; set; } = [.48, 1];
    }

    private sealed record LoadedDirectionTransition(
        string Name,
        DirectionPose Source,
        DirectionPose Target,
        int DurationMs,
        string Easing,
        DirectionTransitionLayerTiming LayerTiming,
        BitmapImage[] BridgeFrames,
        int MissingBridgeCount);

    private sealed class FaceLayerManifest
    {
        public Dictionary<string, FacePoseManifest> Poses { get; set; } = [];
    }

    private sealed class FacePoseManifest
    {
        public int[][] Eyes { get; set; } = [];
        public EarManifest[] Ears { get; set; } = [];
    }

    private sealed class EarManifest
    {
        public string File { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double PivotX { get; set; }
        public double PivotY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed record FacePoseAssets(BitmapImage EyeBase, BitmapImage PupilL, BitmapImage PupilR,
        BitmapImage BlinkClosed, BitmapImage EarL, BitmapImage EarR, FacePoseManifest Layout);

    private sealed class HeadRigManifest
    {
        public Dictionary<string, HeadRigPoseManifest> Poses { get; set; } = [];
    }

    private sealed class HeadRigPoseManifest
    {
        public int[] Pivot { get; set; } = [];
        public int[] UpperBodyPivot { get; set; } = [];
    }

    private sealed record HeadRigAssets(BitmapImage Body, BitmapImage UpperBody, BitmapImage Head, BitmapImage NeckFur,
        BitmapImage ChestFur, HeadRigPoseManifest Layout);

    private static readonly double[] GaitRootMotion =
    [
        0.00,0.15,0.65,1.25,1.75,1.75,1.45,1.00,
        0.00,0.15,0.65,1.25,1.75,1.75,1.45,1.00,
        0.00,0.15,0.65,1.25,1.75,1.75,1.45,1.00,
        0.00,0.15,0.65,1.25,1.75,1.75,1.45,1.00
    ];

    private readonly Random random = new();
    private readonly DispatcherTimer frameTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer decisionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Forms.NotifyIcon trayIcon;
    private readonly FaceParameters face = new();
    private readonly Dictionary<BasePose, FacePoseAssets> faceAssets = [];
    private readonly Dictionary<BasePose, HeadRigAssets> headRigAssets = [];
    private readonly Dictionary<DirectionPose, StandDirectionPoseAssets> standDirectionAssets = [];
    private readonly Dictionary<DirectionPose, DirectionFaceProfile> directionFaceProfiles = [];
    private readonly Dictionary<(DirectionPose Source, DirectionPose Target), LoadedDirectionTransition>
        directionTransitions = [];
    private readonly HashSet<DirectionPose> availableDirectionPoses = [DirectionPose.Center];
    private FaceRuntimeSettings faceSettings = new();
    private BitmapImage[] idle = [], locomotion = [], groom = [], rest = [];
    private BitmapImage[] sleepEnter = [], wakeUp = [], walkTransition = [], curiousGroom = [], yawnFrames = [];
    private BitmapImage[]? activeFrames;
    private int[]? sequence;
    private int sequenceIndex;
    private int currentSequenceFrame;
    private double sequenceFps;
    private bool sequenceLoop;
    private bool sequenceFadeFrames;
    private Action? sequenceCompleted;
    private DateTime nextFrame;
    private DateTime nextDecision;
    private DateTime stateUntil;
    private DateTime bubbleUntil;
    private DateTime nextBlink;
    private DateTime blinkStarted;
    private DateTime nextEarTwitch;
    private DateTime earTwitchStarted;
    private bool doubleBlink;
    private int twitchEar;
    private BasePose? appliedFacePose;
    private bool faceLayerAvailable;
    private bool headRigAvailable;
    private bool directionPoseCatalogAvailable;
    private DirectionPose directionPose = DirectionPose.Center;
    private DirectionPose renderedDirectionPose = DirectionPose.Center;
    private DirectionPose pendingDirectionPose = DirectionPose.Center;
    private DateTime pendingDirectionSince;
    private int pendingDirectionHoldMs;
    private bool manualDirectionOverride;
    private bool directionFadeActive;
    private DateTime directionFadeStarted;
    private DirectionPose directionFadeFrom = DirectionPose.Center;
    private DirectionPose directionFadeTo = DirectionPose.Center;
    private readonly Queue<DirectionPose> queuedDirectionPath = new();
    private readonly Dictionary<DirectionPose, HeadSafeZone> directionHeadSafeZones = [];
    private DirectionHysteresis directionHysteresis = new();
    private double directionFadeSeconds = .12;
    private double transitionHeadLookStrength = .35;
    private double transitionEarLookStrength = .60;
    private double lookStrengthRecoverySmoothing = .14;
    private double currentHeadLookStrength = 1;
    private double currentEarLookStrength = 1;
    private double fadeStartHeadLookStrength = 1;
    private double fadeStartEarLookStrength = 1;
    private double directionFadeProgress;
    private LoadedDirectionTransition? activeDirectionTransition;
    private bool activeDirectionTransitionReverse;
    private double currentEyeCenterX = 400;
    private double currentEyeCenterY = 205;
    private PetState state = PetState.Idle;
    private BasePose basePose = BasePose.Stand;
    private double targetX;
    private double phase;
    private double blend = 1;
    private double fadeSpeed = .32;
    private bool dragging;
    private LocalAgentChatWindow? localAgentChat;
    private System.Windows.Point dragStart;
    private System.Windows.Point windowStart;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        frameTimer.Tick += OnFrame;
        decisionTimer.Tick += OnDecision;

        trayIcon = new Forms.NotifyIcon
        {
            Text = "团团2.5D桌宠",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("叫团团过来", null, (_, _) => Dispatcher.Invoke(Paw));
        menu.Items.Add("低头闻一闻", null, (_, _) => Dispatcher.Invoke(Sniff));
        menu.Items.Add("坐下看看", null, (_, _) => Dispatcher.Invoke(SitAndLook));
        menu.Items.Add("坐着陪你", null, (_, _) => Dispatcher.Invoke(SitStay));
        menu.Items.Add("眨眨眼", null, (_, _) => Dispatcher.Invoke(TriggerBlink));
        menu.Items.Add("舔爪洗脸", null, (_, _) => Dispatcher.Invoke(Groom));
        menu.Items.Add("伸懒腰", null, (_, _) => Dispatcher.Invoke(Stretch));
        menu.Items.Add("睡一会儿", null, (_, _) => Dispatcher.Invoke(Sleep));
        menu.Items.Add("继续散步", null, (_, _) => Dispatcher.Invoke(() => StartWalking(false)));
        var directionMenu = new Forms.ToolStripMenuItem("方向测试");
        directionMenu.DropDownItems.Add("Left", null, (_, _) => Dispatcher.Invoke(() => SetManualDirection(DirectionPose.Left)));
        directionMenu.DropDownItems.Add("Left3Q", null, (_, _) => Dispatcher.Invoke(() => SetManualDirection(DirectionPose.Left3Q)));
        directionMenu.DropDownItems.Add("Center", null, (_, _) => Dispatcher.Invoke(() => SetManualDirection(DirectionPose.Center)));
        directionMenu.DropDownItems.Add("Right3Q", null, (_, _) => Dispatcher.Invoke(() => SetManualDirection(DirectionPose.Right3Q)));
        directionMenu.DropDownItems.Add("Right", null, (_, _) => Dispatcher.Invoke(() => SetManualDirection(DirectionPose.Right)));
        directionMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        directionMenu.DropDownItems.Add("恢复自动", null, (_, _) => Dispatcher.Invoke(EnableAutomaticDirection));
        menu.Items.Add(directionMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(Paw);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "运行状态.log"),
            $"{DateTime.Now:O} v1.18 initialization started\r\n");
        idle = LoadFrames("idle");
        locomotion = LoadFrames("locomotion", true);
        groom = LoadFrames("groom");
        rest = LoadFrames("rest");
        sleepEnter = LoadFrames("sleep_enter", true);
        wakeUp = LoadFrames("wake_up", true);
        walkTransition = LoadFrames("walk_transition", true);
        curiousGroom = LoadFrames("curious_groom", true);
        yawnFrames = LoadFrames("yawn", true);
        LoadFaceLayer();
        LoadHeadRig();
        LoadDirectionPoseCatalog();
        LoadDirectionTransitions();
        SetFrame(idle[0], false);

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 30;
        Top = area.Bottom - Height;
        targetX = Left;
        SetNextDecision(2, 5);
        ScheduleBlink();
        ScheduleEarTwitch();
        frameTimer.Start();
        decisionTimer.Start();
        Say("这次没有黑框啦～", 3);
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "运行状态.log"),
            $"{DateTime.Now:O} v1.18启动，Frames=201，Motion=FootLocked，BasePoses=Stand|Sit|Lie|Sleep，FaceLayer={(faceLayerAvailable ? "Ready" : "Disabled")}，HeadRig={(headRigAvailable ? "Ready" : "Disabled")}，BodyLook=DirectionalTransition，StandDirectionPoses={(directionPoseCatalogAvailable ? string.Join('|', DirectionPoseOrder.Where(availableDirectionPoses.Contains)) : "Disabled")}\r\n");
    }

    private static BitmapImage[] LoadFrames(string category, bool dense = false)
    {
        string root = dense ? "SpritesDense" : "Sprites";
        string folder = Path.Combine(AppContext.BaseDirectory, "Assets", root, category);
        return Directory.GetFiles(folder, "*.png").OrderBy(p => p, StringComparer.Ordinal)
            .Select(LoadBitmap).ToArray();
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void LoadFaceLayer()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Assets", "FaceLayers");
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<FaceLayerManifest>(
                File.ReadAllText(Path.Combine(root, "face_layers.json")), options)
                ?? throw new InvalidDataException("face_layers.json is empty");
            faceSettings = JsonSerializer.Deserialize<FaceRuntimeSettings>(
                File.ReadAllText(Path.Combine(root, "face_runtime.json")), options) ?? new();
            faceAssets[BasePose.Stand] = LoadFacePose(root, "Stand", manifest.Poses["Stand"]);
            faceAssets[BasePose.Sit] = LoadFacePose(root, "Sit", manifest.Poses["Sit"]);
            faceLayerAvailable = true;
        }
        catch (Exception ex)
        {
            faceLayerAvailable = false;
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "运行状态.log"),
                $"{DateTime.Now:O} FaceLayer disabled: {ex.Message}\r\n");
        }
    }

    private static FacePoseAssets LoadFacePose(string root, string pose, FacePoseManifest layout)
    {
        string folder = Path.Combine(root, pose);
        return new(LoadBitmap(Path.Combine(folder, "eye_base.png")),
            LoadBitmap(Path.Combine(folder, "pupil_L.png")),
            LoadBitmap(Path.Combine(folder, "pupil_R.png")),
            LoadBitmap(Path.Combine(folder, "blink_closed.png")),
            LoadBitmap(Path.Combine(folder, "ear_L.png")),
            LoadBitmap(Path.Combine(folder, "ear_R.png")), layout);
    }

    private void LoadHeadRig()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Assets", "HeadRig");
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<HeadRigManifest>(
                File.ReadAllText(Path.Combine(root, "head_rig.json")), options)
                ?? throw new InvalidDataException("head_rig.json is empty");
            headRigAssets[BasePose.Stand] = LoadHeadRigPose(root, "Stand", manifest.Poses["Stand"]);
            headRigAssets[BasePose.Sit] = LoadHeadRigPose(root, "Sit", manifest.Poses["Sit"]);
            headRigAvailable = true;
        }
        catch (Exception ex)
        {
            headRigAvailable = false;
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "运行状态.log"),
                $"{DateTime.Now:O} HeadRig disabled: {ex.Message}\r\n");
        }
    }

    private static HeadRigAssets LoadHeadRigPose(string root, string pose, HeadRigPoseManifest layout)
    {
        string folder = Path.Combine(root, pose);
        return new(LoadBitmap(Path.Combine(folder, "body_base.png")),
            LoadBitmap(Path.Combine(folder, "upper_body.png")),
            LoadBitmap(Path.Combine(folder, "head.png")),
            LoadBitmap(Path.Combine(folder, "neck_fur.png")),
            LoadBitmap(Path.Combine(folder, "chest_fur.png")), layout);
    }

    private void LoadDirectionPoseCatalog()
    {
        string directionRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "DirectionPoses");
        string catalogPath = Path.Combine(directionRoot, "direction_poses.json");
        string layersPath = Path.Combine(directionRoot, "Stand", "direction_layers.json");
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var catalog = JsonSerializer.Deserialize<DirectionPoseManifest>(File.ReadAllText(catalogPath), options)
                ?? throw new InvalidDataException("direction_poses.json is empty");
            var layers = JsonSerializer.Deserialize<DirectionLayerManifest>(File.ReadAllText(layersPath), options)
                ?? throw new InvalidDataException("direction_layers.json is empty");
            if (layers.Canvas.Length < 2 || layers.Canvas[0] != 512 || layers.Canvas[1] != 512)
                throw new InvalidDataException("Stand DirectionPose canvas must be 512x512");

            directionFadeSeconds = Math.Clamp(catalog.CrossfadeDurationMs, 80, 180) / 1000.0;
            transitionHeadLookStrength = Math.Clamp(catalog.TransitionHeadLookStrength, 0, 1);
            transitionEarLookStrength = Math.Clamp(catalog.TransitionEarLookStrength, 0, 1);
            lookStrengthRecoverySmoothing = Math.Clamp(catalog.LookStrengthRecoverySmoothing, .01, 1);
            directionHysteresis = catalog.Hysteresis;
            ValidateDirectionHysteresis(directionHysteresis);
            directionHeadSafeZones.Clear();
            directionFaceProfiles.Clear();
            foreach (DirectionPose pose in DirectionPoseOrder)
            {
                if (!catalog.HeadSafeZones.TryGetValue(pose.ToString(), out var zone))
                    throw new InvalidDataException($"Head Safe Zone is missing for {pose}");
                ValidateHeadSafeZone(pose, zone);
                directionHeadSafeZones[pose] = zone;
                if (!catalog.FaceProfiles.TryGetValue(pose.ToString(), out var profile))
                    throw new InvalidDataException($"Face profile is missing for {pose}");
                profile.PupilLStrength = Math.Clamp(profile.PupilLStrength, 0, 1);
                profile.PupilRStrength = Math.Clamp(profile.PupilRStrength, 0, 1);
                profile.HeadLookStrength = Math.Clamp(profile.HeadLookStrength, 0, 1);
                profile.EarLookStrength = Math.Clamp(profile.EarLookStrength, 0, 1);
                directionFaceProfiles[pose] = profile;
            }

            standDirectionAssets.Clear();
            availableDirectionPoses.Clear();
            var center = BuildCenterDirectionPoseAssets();
            standDirectionAssets[DirectionPose.Center] = center;
            availableDirectionPoses.Add(DirectionPose.Center);
            Stand_Center_Root.SetAssets(center);

            foreach (DirectionPose pose in DirectionPoseOrder.Where(pose => pose != DirectionPose.Center))
            {
                string name = pose.ToString();
                if (!catalog.Poses.TryGetValue(name, out var resource) || !resource.Available)
                {
                    LogDirectionPose($"Stand {name} unavailable in direction_poses.json; Center fallback retained");
                    continue;
                }
                if (!layers.Poses.TryGetValue(name, out var layout))
                {
                    LogDirectionPose($"Stand {name} missing from direction_layers.json; Center fallback retained");
                    continue;
                }

                try
                {
                    var assets = LoadStandDirectionPose(resource, layout);
                    standDirectionAssets[pose] = assets;
                    GetDirectionRoot(pose).SetAssets(assets);
                    availableDirectionPoses.Add(pose);
                }
                catch (Exception ex)
                {
                    LogDirectionPose($"Stand {name} failed validation: {ex.Message}; Center fallback retained");
                }
            }

            SetDirectionRootOpacities(DirectionPose.Center);
            renderedDirectionPose = DirectionPose.Center;
            directionPose = DirectionPose.Center;
            directionPoseCatalogAvailable = true;
            LogDirectionPose($"Stand roots loaded: {string.Join('|', DirectionPoseOrder.Where(availableDirectionPoses.Contains))}; SourcePolicy={layers.SourcePolicy}");
        }
        catch (Exception ex)
        {
            directionPoseCatalogAvailable = false;
            availableDirectionPoses.Clear();
            availableDirectionPoses.Add(DirectionPose.Center);
            StandDirectionPoseHost.Opacity = 0;
            LogDirectionPose($"catalog disabled: {ex.Message}");
        }
    }

    private void LoadDirectionTransitions()
    {
        directionTransitions.Clear();
        string assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
        string path = Path.Combine(assetsRoot, "DirectionPoses", "direction_transitions.json");
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<DirectionTransitionManifest>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("direction_transitions.json is empty");
            foreach ((string name, DirectionTransitionClipManifest clip) in manifest.Clips)
            {
                if (!Enum.TryParse(clip.Source, out DirectionPose source) ||
                    !Enum.TryParse(clip.Target, out DirectionPose target))
                    throw new InvalidDataException($"Transition {name} has an invalid direction");
                if (Math.Abs(DirectionRank(source) - DirectionRank(target)) != 1)
                    throw new InvalidDataException($"Transition {name} is not adjacent");
                ValidateLayerTiming(name, clip.LayerTiming);

                var frames = new List<BitmapImage>();
                int missing = 0;
                foreach (DirectionBridgeFrameManifest bridge in clip.BridgeFrames.OrderBy(frame => frame.At))
                {
                    string file = Path.Combine(assetsRoot,
                        bridge.File.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(file)) frames.Add(LoadBitmap(file));
                    else missing++;
                }
                var loaded = new LoadedDirectionTransition(name, source, target,
                    Math.Clamp(clip.DurationMs, 240, 500), manifest.Easing,
                    clip.LayerTiming, frames.ToArray(), missing);
                directionTransitions[(source, target)] = loaded;
                LogDirectionPose($"transition {name}: durationMs={loaded.DurationMs}, " +
                    $"bridgeFrames={loaded.BridgeFrames.Length}, placeholders={missing}");
            }
            foreach ((DirectionPose source, DirectionPose target) in AdjacentDirectionPairs())
                if (!directionTransitions.ContainsKey((source, target)) &&
                    !directionTransitions.ContainsKey((target, source)))
                    directionTransitions[(source, target)] = CreateFallbackTransition(source, target);
        }
        catch (Exception ex)
        {
            LogDirectionPose($"transition manifest fallback: {ex.Message}");
            foreach ((DirectionPose source, DirectionPose target) in AdjacentDirectionPairs())
                directionTransitions[(source, target)] = CreateFallbackTransition(source, target);
        }
    }

    private static IEnumerable<(DirectionPose Source, DirectionPose Target)> AdjacentDirectionPairs()
    {
        yield return (DirectionPose.Left, DirectionPose.Left3Q);
        yield return (DirectionPose.Left3Q, DirectionPose.Center);
        yield return (DirectionPose.Center, DirectionPose.Right3Q);
        yield return (DirectionPose.Right3Q, DirectionPose.Right);
    }

    private static LoadedDirectionTransition CreateFallbackTransition(DirectionPose source, DirectionPose target)
    {
        int duration = source == DirectionPose.Center || target == DirectionPose.Center ? 320 : 380;
        return new LoadedDirectionTransition($"Fallback_{source}_{target}", source, target, duration,
            "smoothstep", new DirectionTransitionLayerTiming(), [], 2);
    }

    private static void ValidateLayerTiming(string name, DirectionTransitionLayerTiming timing)
    {
        foreach (double[] range in new[]
                 { timing.Eye, timing.Head, timing.Ear, timing.UpperBody, timing.Chest, timing.Body })
        {
            if (range.Length < 2 || range[0] < 0 || range[1] > 1 || range[0] >= range[1])
                throw new InvalidDataException($"Transition {name} has invalid layer timing");
        }
    }

    private StandDirectionPoseAssets BuildCenterDirectionPoseAssets()
    {
        if (!headRigAssets.TryGetValue(BasePose.Stand, out var rig) ||
            !faceAssets.TryGetValue(BasePose.Stand, out var faceAssetsForStand))
            throw new InvalidDataException("Center Stand HeadRig/FaceLayer is unavailable");
        if (rig.Layout.Pivot.Length < 2 || rig.Layout.UpperBodyPivot.Length < 2 ||
            faceAssetsForStand.Layout.Eyes.Length < 2 || faceAssetsForStand.Layout.Ears.Length < 2)
            throw new InvalidDataException("Center Stand anchors are incomplete");

        var ears = faceAssetsForStand.Layout.Ears.Select(ear => new DirectionEarLayout(
            ear.X, ear.Y, ear.Width, ear.Height, ear.PivotX, ear.PivotY)).ToArray();
        var eyes = faceAssetsForStand.Layout.Eyes.Take(2)
            .Select(point => new System.Windows.Point(point[0], point[1])).ToArray();
        return new(rig.Body, rig.UpperBody, rig.Head, rig.NeckFur, rig.ChestFur,
            faceAssetsForStand.EarL, faceAssetsForStand.EarR, faceAssetsForStand.EyeBase,
            faceAssetsForStand.PupilL, faceAssetsForStand.PupilR, faceAssetsForStand.BlinkClosed,
            new System.Windows.Point(rig.Layout.Pivot[0], rig.Layout.Pivot[1]),
            new System.Windows.Point(rig.Layout.UpperBodyPivot[0], rig.Layout.UpperBodyPivot[1]),
            eyes, ears);
    }

    private static void ValidateHeadSafeZone(DirectionPose pose, HeadSafeZone zone)
    {
        if (zone.HeadMinX > zone.HeadMaxX || zone.HeadMinY > zone.HeadMaxY ||
            zone.HeadRotateMin > zone.HeadRotateMax)
            throw new InvalidDataException($"Invalid Head Safe Zone range for {pose}");
    }

    private static void ValidateDirectionHysteresis(DirectionHysteresis value)
    {
        if (value.CenterToThreeQuarter <= 0 || value.ThreeQuarterToSide <= value.CenterToThreeQuarter ||
            value.SideToThreeQuarter >= value.ThreeQuarterToSide ||
            value.ThreeQuarterToCenter >= value.CenterToThreeQuarter ||
            value.ThreeQuarterHoldMs.Length < 2 || value.SideHoldMs.Length < 2)
            throw new InvalidDataException("Invalid five-direction BodyTurn hysteresis");
    }

    private StandDirectionPoseAssets LoadStandDirectionPose(DirectionPoseResource resource,
        DirectionLayerPoseManifest layout)
    {
        if (!layout.CanvasAlignedLayers)
            throw new InvalidDataException("DirectionPose layers must be canvas aligned");
        if (resource.Folder is null)
            throw new InvalidDataException("DirectionPose folder is missing");

        string relativeFolder = resource.Folder.Replace("{BasePose}", "Stand");
        string folder = Path.Combine(AppContext.BaseDirectory, "Assets",
            relativeFolder.Replace('/', Path.DirectorySeparatorChar));
        foreach (string layer in resource.RequiredLayers)
        {
            string file = Path.Combine(folder, $"{layer}.png");
            if (!File.Exists(file)) throw new FileNotFoundException($"Missing DirectionPose layer {layer}", file);
        }
        if (layout.HeadPivot.Length < 2 || layout.UpperBodyPivot.Length < 2 ||
            layout.Eyes.Length < 2 || layout.Ears.Length < 2)
            throw new InvalidDataException("DirectionPose anchors are incomplete");

        var ears = layout.Ears.Take(2).Select(ear =>
        {
            if (ear.Pivot.Length < 2) throw new InvalidDataException("DirectionPose ear pivot is incomplete");
            // DirectionPose ear PNGs are full-canvas aligned.
            return new DirectionEarLayout(0, 0, 512, 512, ear.Pivot[0], ear.Pivot[1]);
        }).ToArray();
        var eyes = layout.Eyes.Take(2).Select(point =>
        {
            if (point.Length < 2) throw new InvalidDataException("DirectionPose eye anchor is incomplete");
            return new System.Windows.Point(point[0], point[1]);
        }).ToArray();

        BitmapImage Layer(string name) => LoadBitmap(Path.Combine(folder, $"{name}.png"));
        return new(Layer("Body_Base"), Layer("UpperBody_Base"), Layer("Head_Base"),
            Layer("Neck_Fur"), Layer("Chest_Fur"), Layer("Ear_L"), Layer("Ear_R"),
            Layer("Eye_Base"), Layer("Pupil_L"), Layer("Pupil_R"), Layer("Blink_Closed"),
            new System.Windows.Point(layout.HeadPivot[0], layout.HeadPivot[1]),
            new System.Windows.Point(layout.UpperBodyPivot[0], layout.UpperBodyPivot[1]), eyes, ears);
    }

    private StandDirectionPoseRoot GetDirectionRoot(DirectionPose pose) => pose switch
    {
        DirectionPose.Left => Stand_Left_Root,
        DirectionPose.Left3Q => Stand_Left3Q_Root,
        DirectionPose.Right3Q => Stand_Right3Q_Root,
        DirectionPose.Right => Stand_Right_Root,
        _ => Stand_Center_Root,
    };

    private void LogDirectionPose(string message)
    {
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "运行状态.log"),
            $"{DateTime.Now:O} DirectionPose {message}\r\n");
    }

    private void SetFrame(BitmapImage frame, bool fade = false)
    {
        if (ReferenceEquals(PetImage.Source, frame))
        {
            UpdateFaceSource(frame);
            return;
        }
        if (fade && PetImage.Source != null)
        {
            PreviousImage.Source = PetImage.Source;
            PreviousImage.Opacity = 1;
            // Keep the new frame fully opaque. Cross-fading both alpha images
            // causes their combined coverage to dip to ~75% at the midpoint,
            // which reads as flashing during a walk cycle.
            PetImage.Opacity = 1;
            blend = 0;
            fadeSpeed = state is PetState.Walking or PetState.FastWalking ? .48 : .30;
        }
        else
        {
            PreviousImage.Opacity = 0;
            PreviousImage.Source = null;
            PetImage.Opacity = 1;
            blend = 1;
        }
        PetImage.Source = frame;
        UpdateFaceSource(frame);
    }

    private void UpdateFaceSource(BitmapImage frame)
    {
        if (!faceLayerAvailable || !headRigAvailable)
        {
            FaceLayerHost.Opacity = 0;
            StandDirectionPoseHost.Opacity = 0;
            PetImage.Opacity = 1;
            return;
        }

        BasePose? pose = null;
        if (idle.Length > 0 && ReferenceEquals(frame, idle[0])) pose = BasePose.Stand;
        else if (curiousGroom.Length > PoseTuning.SitFrame &&
                 ReferenceEquals(frame, curiousGroom[PoseTuning.SitFrame])) pose = BasePose.Sit;

        if (pose is null || !faceAssets.ContainsKey(pose.Value) || state == PetState.Sleeping)
        {
            FaceLayerHost.Opacity = 0;
            StandDirectionPoseHost.Opacity = 0;
            PetImage.Opacity = 1;
            return;
        }

        if (pose == BasePose.Stand && directionPoseCatalogAvailable)
        {
            FaceLayerHost.Opacity = 0;
            StandDirectionPoseHost.Opacity = 1;
            PetImage.Opacity = 0;
            PreviousImage.Opacity = 0;
            PreviousImage.Source = null;
            appliedFacePose = BasePose.Stand;
            return;
        }

        if (appliedFacePose != pose)
            ApplyFacePose(pose.Value);
        StandDirectionPoseHost.Opacity = 0;
        FaceLayerHost.Opacity = 1;
        PetImage.Opacity = 0;
        PreviousImage.Opacity = 0;
        PreviousImage.Source = null;
    }

    private void ApplyFacePose(BasePose pose)
    {
        var assets = faceAssets[pose];
        EyeBaseLayer.Source = assets.EyeBase;
        PupilLLayer.Source = assets.PupilL;
        PupilRLayer.Source = assets.PupilR;
        BlinkClosedLayer.Source = assets.BlinkClosed;
        EarLLayer.Source = assets.EarL;
        EarRLayer.Source = assets.EarR;
        ApplyEarLayout(EarLLayer, assets.Layout.Ears[0]);
        ApplyEarLayout(EarRLayer, assets.Layout.Ears[1]);
        if (assets.Layout.Eyes.Length >= 2)
        {
            currentEyeCenterX = (assets.Layout.Eyes[0][0] + assets.Layout.Eyes[1][0]) * .5;
            currentEyeCenterY = (assets.Layout.Eyes[0][1] + assets.Layout.Eyes[1][1]) * .5;
            ApplyEyeSocketClip(PupilLViewport, assets.Layout.Eyes[0]);
            ApplyEyeSocketClip(PupilRViewport, assets.Layout.Eyes[1]);
        }
        if (headRigAvailable && headRigAssets.TryGetValue(pose, out var rig))
        {
            RigBodyLayer.Source = rig.Body;
            UpperBodyBaseLayer.Source = rig.UpperBody;
            HeadBaseLayer.Source = rig.Head;
            NeckFurLayer.Source = rig.NeckFur;
            ChestFurLayer.Source = rig.ChestFur;
            if (rig.Layout.Pivot.Length >= 2)
            {
                HeadRotateTransform.CenterX = rig.Layout.Pivot[0];
                HeadRotateTransform.CenterY = rig.Layout.Pivot[1];
            }
            if (rig.Layout.UpperBodyPivot.Length >= 2)
            {
                UpperBodySkewTransform.CenterX = rig.Layout.UpperBodyPivot[0];
                UpperBodySkewTransform.CenterY = rig.Layout.UpperBodyPivot[1];
            }
        }
        appliedFacePose = pose;
    }

    private static void ApplyEarLayout(System.Windows.Controls.Image image, EarManifest ear)
    {
        System.Windows.Controls.Canvas.SetLeft(image, ear.X);
        System.Windows.Controls.Canvas.SetTop(image, ear.Y);
        image.Width = ear.Width;
        image.Height = ear.Height;
        image.RenderTransformOrigin = new System.Windows.Point(
            ear.Width <= 0 ? .5 : ear.PivotX / ear.Width,
            ear.Height <= 0 ? .8 : ear.PivotY / ear.Height);
    }

    private static void ApplyEyeSocketClip(System.Windows.Controls.Canvas viewport, int[] center)
    {
        if (center.Length < 2) return;
        viewport.Clip = new System.Windows.Media.EllipseGeometry(
            new System.Windows.Point(center[0], center[1]), 9, 7);
    }

    private void StartSequence(BitmapImage[] frames, int[] indices, double fps, bool loop,
        Action? completed = null, bool fadeFrames = false)
    {
        activeFrames = frames;
        sequence = indices;
        sequenceIndex = 0;
        sequenceFps = fps;
        sequenceLoop = loop;
        sequenceFadeFrames = fadeFrames;
        sequenceCompleted = completed;
        nextFrame = DateTime.MinValue;
    }

    private void StopSequence()
    {
        activeFrames = null;
        sequence = null;
        sequenceCompleted = null;
        sequenceFadeFrames = false;
    }

    private bool AdvanceSequence(DateTime now)
    {
        if (activeFrames == null || sequence == null || now < nextFrame) return false;
        if (sequenceIndex >= sequence.Length)
        {
            if (sequenceLoop) sequenceIndex = 0;
            else
            {
                var completed = sequenceCompleted;
                StopSequence();
                completed?.Invoke();
                return false;
            }
        }
        if (activeFrames == null || sequence == null) return false;
        currentSequenceFrame = Math.Clamp(sequence[sequenceIndex++], 0, activeFrames.Length - 1);
        SetFrame(activeFrames[currentSequenceFrame], sequenceFadeFrames);
        nextFrame = now.AddSeconds(1.0 / sequenceFps);
        return true;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        phase += .016;
        bool frameAdvanced = AdvanceSequence(now);

        if (blend < 1)
        {
            blend = Math.Min(1, blend + fadeSpeed);
            double t = blend * blend * (3 - 2 * blend);
            PetImage.Opacity = 1;
            PreviousImage.Opacity = 1 - t;
            if (blend >= 1)
            {
                PreviousImage.Opacity = 0;
                PreviousImage.Source = null;
            }
        }

        UpdateDirectionTransition(now);
        UpdateFaceLayer(now);

        if (!dragging && state is PetState.Walking or PetState.FastWalking)
        {
            var area = SystemParameters.WorkArea;
            double delta = targetX - Left;
            if (Math.Abs(delta) < 3)
            {
                // Decelerate and plant the paws before returning to idle. This
                // avoids the old locomotion -> idle hard cut.
                state = PetState.Transition;
                StartSequence(walkTransition, Range(20, 9), 22, false, () =>
                {
                    state = PetState.Idle;
                    SetFrame(idle[0]);
                    SetNextDecision(3, 8);
                }, false);
            }
            else if (frameAdvanced)
            {
                // Root motion advances only when the gait pose advances. The
                // former 60 Hz window motion made a frozen paw slide between
                // 36 fps sprite updates. Four contact phases per cycle nearly
                // stop at impact, then accelerate through the push-off.
                double baseStep = state == PetState.FastWalking ? 5.75 : 3.75;
                double step = Math.Min(Math.Abs(delta), baseStep * GaitRootMotion[currentSequenceFrame % GaitRootMotion.Length]);
                Left = Math.Clamp(Left + Math.Sign(delta) * step, area.Left, area.Right - Width);
                PetScale.ScaleX = delta >= 0 ? 1 : -1;
            }
        }

        // The transform origin is bottom-centre, so breathing never changes the
        // registered ground line. Sleeping deliberately holds one sprite.
        double breathing = state == PetState.Sleeping
            ? Math.Sin(phase * 1.25) * PoseTuning.SleepBreathAmplitude
            : state == PetState.Idle && basePose == BasePose.Sit
                ? Math.Sin(phase * 1.5) * PoseTuning.SitBreathAmplitude
                : state == PetState.Idle
                    ? Math.Sin(phase * 1.7) * PoseTuning.StandBreathAmplitude
                    : 0;
        PetScale.ScaleY += ((1 + breathing) - PetScale.ScaleY) * .15;

        if (now > bubbleUntil && SpeechBubble.Opacity > 0)
            SpeechBubble.Opacity = Math.Max(0, SpeechBubble.Opacity - .06);
    }

    private void UpdateFaceLayer(DateTime now)
    {
        if (!faceLayerAvailable || (FaceLayerHost.Opacity <= 0 && StandDirectionPoseHost.Opacity <= 0))
        {
            BlinkClosedLayer.Opacity = 0;
            ApplyStandDirectionFaceState(0);
            return;
        }

        var mouse = Forms.Control.MousePosition;
        var headInWindow = StandDirectionPoseHost.Opacity > 0 && directionPoseCatalogAvailable
            ? GetDirectionRoot(directionFadeActive ? directionFadeTo : renderedDirectionPose).EyePositionIn(this)
            : HeadGroup.TransformToAncestor(this).Transform(
                new System.Windows.Point(currentEyeCenterX, currentEyeCenterY));
        var headScreen = PointToScreen(headInWindow);
        double screenTargetX = Math.Clamp((mouse.X - headScreen.X) / 360.0, -1, 1);
        double targetY = Math.Clamp((mouse.Y - headScreen.Y) / 280.0, -1, 1);
        // Every face transform is authored in sprite-local coordinates.  When
        // the whole pet is mirrored after walking left, local +X points toward
        // screen-left, so convert the screen direction through the facing sign.
        double facingSign = PetScale.ScaleX >= 0 ? 1 : -1;
        double targetX = screenTargetX * facingSign;
        face.MouseX = targetX;
        face.MouseY = targetY;

        // Level 1: the eyes always respond and are the fastest layer.
        face.EyeLookX += (face.MouseX - face.EyeLookX) * faceSettings.EyeSmoothing;
        face.EyeLookY += (face.MouseY - face.EyeLookY) * faceSettings.EyeSmoothing;
        PupilLTransform.X = PupilRTransform.X = face.EyeLookX * faceSettings.EyeLookMaxX;
        PupilLTransform.Y = PupilRTransform.Y = face.EyeLookY * faceSettings.EyeLookMaxY;

        // Level 2: HeadLook starts outside the central 0.30 dead zone.
        double headTargetX = ApplyDeadZone(face.MouseX, faceSettings.HeadActivationThreshold);
        double headTargetY = Math.Abs(face.MouseX) >= faceSettings.HeadActivationThreshold ? face.MouseY : 0;
        face.HeadLookX += (headTargetX - face.HeadLookX) * faceSettings.HeadSmoothing;
        face.HeadLookY += (headTargetY - face.HeadLookY) * faceSettings.HeadSmoothing;
        UpdateTransitionLookStrengths();
        HeadSafeZone safeZone = GetCurrentHeadSafeZone();
        double targetHeadX = Math.Clamp(
            face.HeadLookX * faceSettings.HeadOffsetMaxX * currentHeadLookStrength,
            safeZone.HeadMinX, safeZone.HeadMaxX);
        double targetHeadY = Math.Clamp(
            face.HeadLookY * faceSettings.HeadOffsetMaxY * currentHeadLookStrength,
            safeZone.HeadMinY, safeZone.HeadMaxY);
        double targetHeadRotate = Math.Clamp(
            face.HeadLookX * faceSettings.HeadRotateMaxDegrees * currentHeadLookStrength,
            safeZone.HeadRotateMin, safeZone.HeadRotateMax);
        face.HeadOffsetX += (targetHeadX - face.HeadOffsetX) * faceSettings.HeadSmoothing;
        face.HeadOffsetY += (targetHeadY - face.HeadOffsetY) * faceSettings.HeadSmoothing;
        face.HeadRotate += (targetHeadRotate - face.HeadRotate) * faceSettings.HeadSmoothing;
        face.HeadOffsetX = Math.Clamp(face.HeadOffsetX, safeZone.HeadMinX, safeZone.HeadMaxX);
        face.HeadOffsetY = Math.Clamp(face.HeadOffsetY, safeZone.HeadMinY, safeZone.HeadMaxY);
        face.HeadRotate = Math.Clamp(face.HeadRotate, safeZone.HeadRotateMin, safeZone.HeadRotateMax);
        HeadTranslateTransform.X = face.HeadOffsetX;
        HeadTranslateTransform.Y = face.HeadOffsetY;
        HeadRotateTransform.Angle = face.HeadRotate;

        // Level 3: ChestLook uses only a small translation and skew.  It does
        // not rotate the full body sprite or alter the planted paws.
        double chestTarget = ApplyDeadZone(face.MouseX, faceSettings.BodyActivationThreshold);
        face.ChestLookX += (chestTarget - face.ChestLookX) * faceSettings.ChestSmoothing;
        double chestActivity = Math.Abs(face.ChestLookX);
        double targetUpperX = face.ChestLookX * faceSettings.ChestOffsetMaxX;
        double targetUpperY = face.MouseY * chestActivity * faceSettings.ChestOffsetMaxY;
        double targetUpperSkew = face.ChestLookX * faceSettings.ChestSkewMaxDegrees;
        face.UpperBodyOffsetX += (targetUpperX - face.UpperBodyOffsetX) * faceSettings.ChestSmoothing;
        face.UpperBodyOffsetY += (targetUpperY - face.UpperBodyOffsetY) * faceSettings.ChestSmoothing;
        face.UpperBodyRotate += (targetUpperSkew - face.UpperBodyRotate) * faceSettings.ChestSmoothing;
        UpperBodyTranslateTransform.X = face.UpperBodyOffsetX;
        UpperBodyTranslateTransform.Y = face.UpperBodyOffsetY;
        UpperBodySkewTransform.AngleX = face.UpperBodyRotate;

        // Level 4: BodyLook is a slow normalized signal used only by the
        // DirectionPose state machine.  No fake whole-sprite Z rotation.
        face.BodyLookTargetX = Math.Abs(face.MouseX) >= faceSettings.HeadActivationThreshold
            ? face.MouseX
            : 0;
        face.BodyLookX += (face.BodyLookTargetX - face.BodyLookX) * faceSettings.BodySmoothing;
        if (basePose == BasePose.Stand && StandDirectionPoseHost.Opacity > 0)
            UpdateBodyTurnState(now);

        if (now >= nextBlink && blinkStarted == default)
        {
            blinkStarted = now;
            doubleBlink = random.NextDouble() < faceSettings.DoubleBlinkChance;
        }
        face.Blink = BlinkAmount(now);
        BlinkClosedLayer.Opacity = face.Blink;

        if (!directionFadeActive && now >= nextEarTwitch && earTwitchStarted == default)
        {
            earTwitchStarted = now;
            twitchEar = random.Next(3); // left, right, or both
        }
        if (directionFadeActive) earTwitchStarted = default;
        double twitch = directionFadeActive ? 0 : EarTwitchAmount(now);
        double mouseEar = face.HeadLookX * faceSettings.EarMouseMaxDegrees * currentEarLookStrength;
        double leftTarget = mouseEar + (twitchEar is 0 or 2 ? twitch : 0);
        double rightTarget = mouseEar * .65 - (twitchEar is 1 or 2 ? twitch : 0);
        face.EarLRotate += (leftTarget - face.EarLRotate) * faceSettings.EarSmoothing;
        face.EarRRotate += (rightTarget - face.EarRRotate) * faceSettings.EarSmoothing;
        EarLRotate.Angle = face.EarLRotate;
        EarRRotate.Angle = face.EarRRotate;
        ApplyStandDirectionFaceState(face.Blink);
    }

    private void UpdateTransitionLookStrengths()
    {
        if (directionFadeActive)
        {
            // Reach the safe low-strength plateau during the first half of the
            // transition, then hold it until the body direction has settled.
            double down = SmoothStep(Math.Clamp(directionFadeProgress * 2, 0, 1));
            currentHeadLookStrength = fadeStartHeadLookStrength +
                (transitionHeadLookStrength - fadeStartHeadLookStrength) * down;
            currentEarLookStrength = fadeStartEarLookStrength +
                (transitionEarLookStrength - fadeStartEarLookStrength) * down;
            return;
        }

        currentHeadLookStrength += (1 - currentHeadLookStrength) * lookStrengthRecoverySmoothing;
        currentEarLookStrength += (1 - currentEarLookStrength) * lookStrengthRecoverySmoothing;
    }

    private HeadSafeZone GetCurrentHeadSafeZone()
    {
        HeadSafeZone Zone(DirectionPose pose) => directionHeadSafeZones.TryGetValue(pose, out var zone)
            ? zone
            : new HeadSafeZone();
        if (!directionFadeActive) return Zone(renderedDirectionPose);

        HeadSafeZone source = Zone(directionFadeFrom);
        HeadSafeZone target = Zone(directionFadeTo);
        return new HeadSafeZone
        {
            HeadMinX = Math.Max(source.HeadMinX, target.HeadMinX),
            HeadMaxX = Math.Min(source.HeadMaxX, target.HeadMaxX),
            HeadMinY = Math.Max(source.HeadMinY, target.HeadMinY),
            HeadMaxY = Math.Min(source.HeadMaxY, target.HeadMaxY),
            HeadRotateMin = Math.Max(source.HeadRotateMin, target.HeadRotateMin),
            HeadRotateMax = Math.Min(source.HeadRotateMax, target.HeadRotateMax),
        };
    }

    private void ApplyStandDirectionFaceState(double blink)
    {
        double eyeX = face.EyeLookX * faceSettings.EyeLookMaxX;
        double eyeY = face.EyeLookY * faceSettings.EyeLookMaxY;
        foreach (DirectionPose pose in standDirectionAssets.Keys)
        {
            DirectionFaceProfile profile = directionFaceProfiles.TryGetValue(pose, out var configured)
                ? configured
                : new DirectionFaceProfile();
            GetDirectionRoot(pose).ApplyMotion(
                eyeX * profile.PupilLStrength, eyeY * profile.PupilLStrength,
                eyeX * profile.PupilRStrength, eyeY * profile.PupilRStrength,
                face.HeadOffsetX * profile.HeadLookStrength,
                face.HeadOffsetY * profile.HeadLookStrength,
                face.HeadRotate * profile.HeadLookStrength,
                face.UpperBodyOffsetX, face.UpperBodyOffsetY, face.UpperBodyRotate,
                blink,
                face.EarLRotate * profile.EarLookStrength,
                face.EarRRotate * profile.EarLookStrength);
        }
    }

    private void UpdateBodyTurnState(DateTime now)
    {
        if (manualDirectionOverride || basePose != BasePose.Stand || !directionPoseCatalogAvailable)
            return;
        // Mouse/eye/head signals continue updating while the current turn
        // finishes, but a second body transition cannot interrupt it.
        if (directionFadeActive) return;
        DirectionPose desired = directionPose;
        double look = face.BodyLookX;

        // Every decision moves exactly one adjacent step.  A complete reversal
        // therefore has to traverse Right -> Right3Q -> Center -> Left3Q -> Left.
        if (directionPose == DirectionPose.Center)
        {
            if (look > directionHysteresis.CenterToThreeQuarter) desired = DirectionPose.Right3Q;
            else if (look < -directionHysteresis.CenterToThreeQuarter) desired = DirectionPose.Left3Q;
        }
        else if (directionPose == DirectionPose.Right3Q)
        {
            if (look > directionHysteresis.ThreeQuarterToSide) desired = DirectionPose.Right;
            else if (look < directionHysteresis.ThreeQuarterToCenter) desired = DirectionPose.Center;
        }
        else if (directionPose == DirectionPose.Right && look < directionHysteresis.SideToThreeQuarter)
            desired = DirectionPose.Right3Q;
        else if (directionPose == DirectionPose.Left3Q)
        {
            if (look < -directionHysteresis.ThreeQuarterToSide) desired = DirectionPose.Left;
            else if (look > -directionHysteresis.ThreeQuarterToCenter) desired = DirectionPose.Center;
        }
        else if (directionPose == DirectionPose.Left && look > -directionHysteresis.SideToThreeQuarter)
            desired = DirectionPose.Left3Q;

        if (desired == directionPose)
        {
            pendingDirectionPose = directionPose;
            pendingDirectionSince = default;
            return;
        }

        if (pendingDirectionSince == default || pendingDirectionPose != desired)
        {
            pendingDirectionPose = desired;
            pendingDirectionSince = now;
            int[] holdRange = Math.Abs(DirectionRank(desired)) == 2 ||
                              Math.Abs(DirectionRank(directionPose)) == 2
                ? directionHysteresis.SideHoldMs
                : directionHysteresis.ThreeQuarterHoldMs;
            pendingDirectionHoldMs = random.Next(
                Math.Min(holdRange[0], holdRange[1]),
                Math.Max(holdRange[0], holdRange[1]) + 1);
            return;
        }

        if ((now - pendingDirectionSince).TotalMilliseconds < pendingDirectionHoldMs) return;
        directionPose = desired;
        pendingDirectionSince = default;
        ApplyDirectionPoseResource(directionPose);
    }

    private void ApplyDirectionPoseResource(DirectionPose pose)
    {
        DirectionPose target = availableDirectionPoses.Contains(pose) && standDirectionAssets.ContainsKey(pose)
            ? pose
            : DirectionPose.Center;
        if (target != pose)
            LogDirectionPose($"requested {pose}, resource unavailable; safely falling back to Center");
        if (target == renderedDirectionPose && !directionFadeActive)
        {
            SetDirectionRootOpacities(target);
            return;
        }

        directionFadeFrom = renderedDirectionPose;
        if (!availableDirectionPoses.Contains(directionFadeFrom)) directionFadeFrom = DirectionPose.Center;
        directionFadeTo = target;
        if (!TryGetDirectionTransition(directionFadeFrom, directionFadeTo,
                out LoadedDirectionTransition? transition, out bool reverse))
        {
            transition = CreateFallbackTransition(directionFadeFrom, directionFadeTo);
            reverse = false;
        }
        LoadedDirectionTransition selectedTransition = transition
            ?? CreateFallbackTransition(directionFadeFrom, directionFadeTo);
        activeDirectionTransition = selectedTransition;
        activeDirectionTransitionReverse = reverse;
        directionFadeSeconds = selectedTransition.DurationMs / 1000.0;
        directionFadeStarted = DateTime.Now;
        directionFadeActive = directionFadeFrom != directionFadeTo;
        directionFadeProgress = directionFadeActive ? 0 : 1;
        if (directionFadeActive)
        {
            // Preserve continuity when an ordered Left/Center/Right transition
            // immediately starts its next leg.
            fadeStartHeadLookStrength = currentHeadLookStrength;
            fadeStartEarLookStrength = currentEarLookStrength;
            earTwitchStarted = default;
            nextEarTwitch = DateTime.MaxValue;
        }
        if (!directionFadeActive)
        {
            renderedDirectionPose = target;
            SetDirectionRootOpacities(target);
        }
        string mode = selectedTransition.MissingBridgeCount == 0 && selectedTransition.BridgeFrames.Length > 0
            ? "bridge-clip"
            : "layer-stagger-placeholder";
        LogDirectionPose($"transition start clip={selectedTransition.Name}, from={directionFadeFrom}, " +
            $"to={target}, reverse={reverse}, mode={mode}, durationMs={selectedTransition.DurationMs}");
    }

    private bool TryGetDirectionTransition(DirectionPose source, DirectionPose target,
        out LoadedDirectionTransition? transition, out bool reverse)
    {
        if (directionTransitions.TryGetValue((source, target), out transition))
        {
            reverse = false;
            return true;
        }
        if (directionTransitions.TryGetValue((target, source), out transition))
        {
            reverse = true;
            return true;
        }
        reverse = false;
        return false;
    }

    private void UpdateDirectionTransition(DateTime now)
    {
        if (!directionFadeActive || activeDirectionTransition is null) return;
        double raw = Math.Clamp((now - directionFadeStarted).TotalSeconds / directionFadeSeconds, 0, 1);
        directionFadeProgress = raw;

        if (activeDirectionTransition.MissingBridgeCount == 0 &&
            activeDirectionTransition.BridgeFrames.Length > 0)
            ApplyBridgeTransition(raw, activeDirectionTransition);
        else
            ApplyLayeredPlaceholderTransition(raw, activeDirectionTransition);

        if (raw < 1) return;
        renderedDirectionPose = directionFadeTo;
        directionFadeActive = false;
        directionFadeProgress = 1;
        activeDirectionTransition = null;
        ResetDirectionTransitionVisuals();
        SetDirectionRootOpacities(renderedDirectionPose);
        pendingDirectionSince = default;
        ScheduleEarTwitch();
        LogDirectionPose($"transition complete rendered={renderedDirectionPose}, latestBodyTarget={face.BodyLookTargetX:0.00}");

        StartNextQueuedDirection();
    }

    private void ApplyLayeredPlaceholderTransition(double raw, LoadedDirectionTransition clip)
    {
        ResetDirectionTransitionVisuals();
        StandDirectionPoseRoot source = GetDirectionRoot(directionFadeFrom);
        StandDirectionPoseRoot target = GetDirectionRoot(directionFadeTo);
        source.Opacity = target.Opacity = 1;

        double eye = TransitionLayerProgress(raw, clip.LayerTiming.Eye);
        double head = TransitionLayerProgress(raw, clip.LayerTiming.Head);
        double ear = TransitionLayerProgress(raw, clip.LayerTiming.Ear);
        double upper = TransitionLayerProgress(raw, clip.LayerTiming.UpperBody);
        double chest = TransitionLayerProgress(raw, clip.LayerTiming.Chest);
        double body = TransitionLayerProgress(raw, clip.LayerTiming.Body);
        source.ApplyTransitionWeights(1 - body, 1 - upper, 1 - chest, 1 - head, 1 - ear, 1 - eye);
        target.ApplyTransitionWeights(body, upper, chest, head, ear, eye);
    }

    private double TransitionLayerProgress(double raw, double[] range)
    {
        double Evaluate(double value)
        {
            double local = Math.Clamp((value - range[0]) / (range[1] - range[0]), 0, 1);
            return SmoothStep(local);
        }
        return activeDirectionTransitionReverse ? 1 - Evaluate(1 - raw) : Evaluate(raw);
    }

    private void ApplyBridgeTransition(double raw, LoadedDirectionTransition clip)
    {
        ResetDirectionTransitionVisuals();
        BitmapImage[] frames = activeDirectionTransitionReverse
            ? clip.BridgeFrames.Reverse().ToArray()
            : clip.BridgeFrames;
        double position = SmoothStep(raw) * (frames.Length + 1);
        int segment = Math.Min(frames.Length, (int)Math.Floor(position));
        double local = SmoothStep(position - segment);
        ShowTransitionKey(segment, 1 - local, frames, true);
        ShowTransitionKey(segment + 1, local, frames, false);
    }

    private void ShowTransitionKey(int index, double opacity, BitmapImage[] frames, bool firstSlot)
    {
        if (opacity <= 0) return;
        if (index == 0)
        {
            GetDirectionRoot(directionFadeFrom).Opacity += opacity;
            return;
        }
        if (index == frames.Length + 1)
        {
            GetDirectionRoot(directionFadeTo).Opacity += opacity;
            return;
        }
        System.Windows.Controls.Image image = firstSlot ? DirectionBridgeFromImage : DirectionBridgeToImage;
        image.Source = frames[index - 1];
        image.Opacity = opacity;
    }

    private void ResetDirectionTransitionVisuals()
    {
        foreach (DirectionPose pose in DirectionPoseOrder)
        {
            StandDirectionPoseRoot root = GetDirectionRoot(pose);
            root.Opacity = 0;
            root.ResetTransitionWeights();
        }
        DirectionBridgeFromImage.Opacity = DirectionBridgeToImage.Opacity = 0;
        DirectionBridgeFromImage.Source = DirectionBridgeToImage.Source = null;
    }

    private void SetDirectionRootOpacities(DirectionPose visible)
    {
        Stand_Left_Root.Opacity = visible == DirectionPose.Left ? 1 : 0;
        Stand_Left3Q_Root.Opacity = visible == DirectionPose.Left3Q ? 1 : 0;
        Stand_Center_Root.Opacity = visible == DirectionPose.Center ? 1 : 0;
        Stand_Right3Q_Root.Opacity = visible == DirectionPose.Right3Q ? 1 : 0;
        Stand_Right_Root.Opacity = visible == DirectionPose.Right ? 1 : 0;
    }

    private void SetManualDirection(DirectionPose target)
    {
        manualDirectionOverride = true;
        pendingDirectionSince = default;
        pendingDirectionPose = target;
        queuedDirectionPath.Clear();
        DirectionPose cursor = directionFadeActive ? directionFadeTo : renderedDirectionPose;
        while (cursor != target)
        {
            cursor = StepToward(cursor, target);
            queuedDirectionPath.Enqueue(cursor);
        }
        directionPose = target;
        if (!directionFadeActive) StartNextQueuedDirection();
        Say($"方向测试：{target}", 1.5);
    }

    private void StartNextQueuedDirection()
    {
        if (directionFadeActive || queuedDirectionPath.Count == 0) return;
        ApplyDirectionPoseResource(queuedDirectionPath.Dequeue());
    }

    private static int DirectionRank(DirectionPose pose) => Array.IndexOf(DirectionPoseOrder, pose) - 2;

    private static DirectionPose StepToward(DirectionPose from, DirectionPose target)
    {
        int fromIndex = Array.IndexOf(DirectionPoseOrder, from);
        int targetIndex = Array.IndexOf(DirectionPoseOrder, target);
        if (fromIndex == targetIndex) return from;
        return DirectionPoseOrder[fromIndex + Math.Sign(targetIndex - fromIndex)];
    }

    private void EnableAutomaticDirection()
    {
        manualDirectionOverride = false;
        queuedDirectionPath.Clear();
        pendingDirectionSince = default;
        directionPose = renderedDirectionPose;
        LogDirectionPose("manual override disabled; BodyTurn automatic state machine resumed");
        Say("方向跟随已恢复", 1.5);
    }

    private static double ApplyDeadZone(double value, double threshold)
    {
        double magnitude = Math.Abs(value);
        if (magnitude <= threshold) return 0;
        return Math.Sign(value) * Math.Clamp((magnitude - threshold) / Math.Max(.001, 1 - threshold), 0, 1);
    }

    private double BlinkAmount(DateTime now)
    {
        if (blinkStarted == default) return 0;
        double close = faceSettings.BlinkCloseSeconds;
        double open = faceSettings.BlinkOpenSeconds;
        double pulse = close + open;
        double elapsed = (now - blinkStarted).TotalSeconds;
        double amount;
        if (elapsed < close) amount = SmoothStep(elapsed / close);
        else if (elapsed < pulse) amount = 1 - SmoothStep((elapsed - close) / open);
        else if (doubleBlink && elapsed < pulse + .09) amount = 0;
        else if (doubleBlink && elapsed < pulse + .09 + close)
            amount = SmoothStep((elapsed - pulse - .09) / close);
        else if (doubleBlink && elapsed < pulse * 2 + .09)
            amount = 1 - SmoothStep((elapsed - pulse - .09 - close) / open);
        else
        {
            blinkStarted = default;
            ScheduleBlink();
            amount = 0;
        }
        return Math.Clamp(amount, 0, 1);
    }

    private double EarTwitchAmount(DateTime now)
    {
        if (earTwitchStarted == default) return 0;
        double elapsed = (now - earTwitchStarted).TotalSeconds;
        if (elapsed >= .32)
        {
            earTwitchStarted = default;
            ScheduleEarTwitch();
            return 0;
        }
        return Math.Sin(elapsed / .32 * Math.PI * 2) * faceSettings.EarTwitchDegrees;
    }

    private void ScheduleBlink()
    {
        double seconds = faceSettings.BlinkMinSeconds + random.NextDouble() *
            Math.Max(.1, faceSettings.BlinkMaxSeconds - faceSettings.BlinkMinSeconds);
        nextBlink = DateTime.Now.AddSeconds(seconds);
    }

    private void ScheduleEarTwitch()
    {
        double seconds = faceSettings.EarTwitchMinSeconds + random.NextDouble() *
            Math.Max(.1, faceSettings.EarTwitchMaxSeconds - faceSettings.EarTwitchMinSeconds);
        nextEarTwitch = DateTime.Now.AddSeconds(seconds);
    }

    private static double SmoothStep(double value)
    {
        double t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private void OnDecision(object? sender, EventArgs e)
    {
        if (dragging || DateTime.Now < nextDecision || state is PetState.Grooming or PetState.Transition or PetState.Playing)
            return;
        if (state == PetState.Sleeping)
        {
            if (DateTime.Now >= stateUntil) Wake();
            return;
        }
        if (state is PetState.Walking or PetState.FastWalking) return;

        int roll = random.Next(100);
        if (roll < 25) StartWalking(false);
        else if (roll < 32) StartWalking(true);
        else if (roll < 48) Groom();
        else if (roll < 60) Stretch();
        else if (roll < 69) Yawn();
        else if (roll < 82) Sleep();
        else IdleMicro();
    }

    private void IdleMicro()
    {
        if (basePose != BasePose.Stand)
        {
            TransitionToBasePose(BasePose.Stand, IdleMicro);
            return;
        }
        state = PetState.Idle;
        int[][] variants = [
            [0,1,0,1,0], [0,2,3,2,0], [0,4,4,0],
            [0,5,5,0], [0,6,6,0], [0,7,0]
        ];
        StartSequence(idle, variants[random.Next(variants.Length)], 5.5, false,
            () => { SetFrame(idle[0]); SetNextDecision(2, 6); });
        SetNextDecision(3, 7);
    }

    private void StartWalking(bool fast)
    {
        if (basePose != BasePose.Stand)
        {
            TransitionToBasePose(BasePose.Stand, () => StartWalking(fast));
            return;
        }
        var area = SystemParameters.WorkArea;
        targetX = area.Left + random.NextDouble() * Math.Max(1, area.Width - Width);
        double delta = targetX - Left;
        PetScale.ScaleX = delta >= 0 ? 1 : -1;
        state = PetState.Transition;
        // Look, shift weight and take the first step before entering the loop.
        StartSequence(walkTransition, Range(0, 13), fast ? 30 : 24, false, () =>
        {
            state = fast ? PetState.FastWalking : PetState.Walking;
            StartSequence(locomotion, All(locomotion), fast ? 52 : 36, true, null, false);
        }, false);
        SetNextDecision(12, 20);
    }

    private void Groom()
    {
        state = PetState.Grooming;
        TransitionToBasePose(BasePose.Sit, () =>
        {
            int actionStart = Math.Min(PoseTuning.SitFrame + 1, curiousGroom.Length - 1);
            StartSequence(curiousGroom, Range(actionStart, curiousGroom.Length - actionStart), 18, false, () =>
            {
                basePose = BasePose.Sit;
                TransitionToBasePose(BasePose.Stand, FinishAtStand);
            }, false);
        });
    }

    private void Stretch()
    {
        if (basePose != BasePose.Stand)
        {
            TransitionToBasePose(BasePose.Stand, Stretch);
            return;
        }
        state = PetState.Playing;
        // Lower the body, bow into a full stretch, then gather the paws and rise.
        int[] lower = Enumerable.Range(14, 15).Reverse().ToArray();
        int[] rise = Range(14, 15);
        StartSequence(wakeUp, lower.Concat([14,14]).Concat(rise).ToArray(), 20, false,
            () => { state = PetState.Idle; SetFrame(idle[0]); SetNextDecision(3, 7); }, false);
    }

    private void Yawn()
    {
        state = PetState.Playing;
        TransitionToBasePose(BasePose.Sit, () =>
        {
            StartSequence(yawnFrames, All(yawnFrames), 18, false, () =>
            {
                basePose = BasePose.Sit;
                TransitionToBasePose(BasePose.Stand, FinishAtStand);
            }, false);
        });
    }

    private void Sleep()
    {
        if (basePose != BasePose.Stand)
        {
            TransitionToBasePose(BasePose.Stand, Sleep);
            return;
        }
        state = PetState.Transition;
        Say("呼……zzz", 2.5);
        StartSequence(sleepEnter, All(sleepEnter), 16, false, () =>
        {
            basePose = BasePose.Sleep;
            state = PetState.Sleeping;
            SetFrame(sleepEnter[7]);
            StopSequence();
            stateUntil = DateTime.Now.AddSeconds(random.Next(10, 20));
            SetNextDecision(1, 2);
        }, false);
    }

    private void Wake()
    {
        state = PetState.Transition;
        StartSequence(wakeUp, All(wakeUp), 16, false, () =>
        {
            basePose = BasePose.Stand;
            state = PetState.Idle;
            SetFrame(idle[0]);
            Say("睡醒啦！", 2);
            SetNextDecision(3, 7);
        }, false);
    }

    private void Paw()
    {
        if (basePose != BasePose.Stand)
        {
            TransitionToBasePose(BasePose.Stand, Paw);
            return;
        }
        state = PetState.Playing;
        Say(random.Next(2) == 0 ? "喵～" : "抓到你啦", 2);
        int[] reach = Range(0, PoseTuning.PawReachFrame + 1);
        StartSequence(curiousGroom, reach.Concat(reach.Reverse().Skip(1)).ToArray(), 24, false, () =>
        {
            state = PetState.Idle;
            SetFrame(idle[0]);
            SetNextDecision(3, 7);
        }, false);
    }

    private void Sniff()
    {
        if (basePose != BasePose.Stand)
        {
            TransitionToBasePose(BasePose.Stand, Sniff);
            return;
        }
        state = PetState.Playing;
        int[] sniff = Range(0, PoseTuning.SniffFrame + 1);
        StartSequence(curiousGroom, sniff.Concat([4,4]).Concat(sniff.Reverse().Skip(1)).ToArray(), 18, false, () =>
        {
            state = PetState.Idle;
            SetFrame(idle[0]);
            SetNextDecision(2, 6);
        }, false);
    }

    private void SitAndLook()
    {
        state = PetState.Playing;
        TransitionToBasePose(BasePose.Sit, () =>
        {
            int[] look = Range(PoseTuning.SitFrame, PoseTuning.SitLookFrame - PoseTuning.SitFrame + 1);
            StartSequence(curiousGroom,
                look.Concat([PoseTuning.SitLookFrame, PoseTuning.SitLookFrame])
                    .Concat(look.Reverse().Skip(1)).ToArray(), 18, false, () =>
                {
                    basePose = BasePose.Sit;
                    TransitionToBasePose(BasePose.Stand, FinishAtStand);
                }, false);
        });
    }

    private void SitStay()
    {
        state = PetState.Playing;
        TransitionToBasePose(BasePose.Sit, () =>
        {
            basePose = BasePose.Sit;
            state = PetState.Idle;
            SetFrame(curiousGroom[PoseTuning.SitFrame]);
            Say("陪你坐一会儿～", 2);
            SetNextDecision(8, 12);
        });
    }

    private void TriggerBlink()
    {
        if (!faceLayerAvailable || FaceLayerHost.Opacity <= 0) return;
        doubleBlink = random.NextDouble() < faceSettings.DoubleBlinkChance;
        blinkStarted = DateTime.Now;
    }

    private void TransitionToBasePose(BasePose target, Action completed)
    {
        if (basePose == target)
        {
            completed();
            return;
        }

        state = PetState.Transition;
        if (basePose == BasePose.Stand && target == BasePose.Sit)
        {
            StartSequence(curiousGroom, Range(0, PoseTuning.SitFrame + 1),
                PoseTuning.StandSitFps, false, () =>
                {
                    basePose = BasePose.Sit;
                    SetFrame(curiousGroom[PoseTuning.SitFrame]);
                    completed();
                }, false);
            return;
        }

        if (basePose == BasePose.Sit && target == BasePose.Stand)
        {
            StartSequence(curiousGroom, Range(0, PoseTuning.SitFrame + 1).Reverse().ToArray(),
                PoseTuning.StandSitFps, false, () =>
                {
                    basePose = BasePose.Stand;
                    SetFrame(idle[0]);
                    completed();
                }, false);
            return;
        }

        // Lie and Sleep currently use full-frame authored transitions. Returning
        // to Stand is the safe neutral bridge for later layered pose expansion.
        basePose = BasePose.Stand;
        SetFrame(idle[0]);
        completed();
    }

    private void FinishAtStand()
    {
        basePose = BasePose.Stand;
        state = PetState.Idle;
        SetFrame(idle[0]);
        SetNextDecision(3, 7);
    }

    private static int[] All(BitmapImage[] frames) => Range(0, frames.Length);

    private static int[] Range(int start, int count) => Enumerable.Range(start, count).ToArray();

    private void Say(string text, double seconds)
    {
        SpeechText.Text = text;
        SpeechBubble.Opacity = 1;
        bubbleUntil = DateTime.Now.AddSeconds(seconds);
    }

    private void SetNextDecision(int minSeconds, int maxSeconds) =>
        nextDecision = DateTime.Now.AddSeconds(random.Next(minSeconds, maxSeconds + 1));

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        localAgentChat?.SetPetHover(false);
        dragging = true;
        dragStart = PointToScreen(e.GetPosition(this));
        windowStart = new System.Windows.Point(Left, Top);
        PetImage.CaptureMouse();
        StopSequence();
        SetFrame(rest[7]);
    }

    private void Pet_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var current = PointToScreen(e.GetPosition(this));
        Left = windowStart.X + current.X - dragStart.X;
        Top = windowStart.Y + current.Y - dragStart.Y;
        PetRotate.Angle = Math.Clamp((current.X - dragStart.X) / 12, -8, 8);
    }

    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        double moved = Math.Abs(Left - windowStart.X) + Math.Abs(Top - windowStart.Y);
        dragging = false;
        PetImage.ReleaseMouseCapture();
        PetRotate.Angle = 0;
        Top = SystemParameters.WorkArea.Bottom - Height;
        state = PetState.Idle;
        basePose = BasePose.Stand;
        SetFrame(idle[0]);
        if (moved < 10) Paw();
        else { Say("放在这里也可以～", 2); SetNextDecision(3, 7); }
    }

    private void Pet_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        AddMenu(menu, "和团团聊聊（本地 AI）", OpenLocalAgentChat);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddMenu(menu, "挥爪", Paw);
        AddMenu(menu, "低头闻一闻", Sniff);
        AddMenu(menu, "坐下看看", SitAndLook);
        AddMenu(menu, "坐着陪你", SitStay);
        AddMenu(menu, "眨眨眼", TriggerBlink);
        AddMenu(menu, "舔爪洗脸", Groom);
        AddMenu(menu, "伸懒腰", Stretch);
        AddMenu(menu, "睡觉", Sleep);
        AddMenu(menu, "散步", () => StartWalking(false));
        var directionMenu = new System.Windows.Controls.MenuItem { Header = "方向测试" };
        AddMenu(directionMenu, "Left", () => SetManualDirection(DirectionPose.Left));
        AddMenu(directionMenu, "Left3Q", () => SetManualDirection(DirectionPose.Left3Q));
        AddMenu(directionMenu, "Center", () => SetManualDirection(DirectionPose.Center));
        AddMenu(directionMenu, "Right3Q", () => SetManualDirection(DirectionPose.Right3Q));
        AddMenu(directionMenu, "Right", () => SetManualDirection(DirectionPose.Right));
        directionMenu.Items.Add(new System.Windows.Controls.Separator());
        AddMenu(directionMenu, "恢复自动", EnableAutomaticDirection);
        menu.Items.Add(directionMenu);
        menu.Items.Add(new System.Windows.Controls.Separator());
        AddMenu(menu, "退出桌宠", Close);
        menu.IsOpen = true;
    }

    private void OpenLocalAgentChat()
    {
        localAgentChat ??= new LocalAgentChatWindow(PerformAgentPetAction) { Owner = this };
        localAgentChat.OpenPinned();
    }

    private void Pet_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (dragging) return;
        localAgentChat ??= new LocalAgentChatWindow(PerformAgentPetAction) { Owner = this };
        localAgentChat.SetPetHover(true);
        localAgentChat.ShowForPetHover();
    }

    private void Pet_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        localAgentChat?.SetPetHover(false);

    private void PerformAgentPetAction(string action)
    {
        switch (action)
        {
            case "paw": Paw(); break;
            case "sniff": Sniff(); break;
            case "sit": SitStay(); break;
            case "sleep": Sleep(); break;
            case "wake": Wake(); break;
            case "walk": StartWalking(false); break;
            case "groom": Groom(); break;
            case "stretch": Stretch(); break;
            case "blink": TriggerBlink(); break;
            default: return;
        }
    }

    private static void AddMenu(System.Windows.Controls.ContextMenu menu, string title, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = title };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static void AddMenu(System.Windows.Controls.MenuItem menu, string title, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = title };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        localAgentChat?.Close();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }
}
