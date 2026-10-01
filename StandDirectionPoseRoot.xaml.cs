using System.Windows;
using System.Windows.Media;

namespace RagdollPet;

public partial class StandDirectionPoseRoot
{
    private double eyeCenterX = 400;
    private double eyeCenterY = 205;
    private double transitionEyeWeight = 1;

    public StandDirectionPoseRoot()
    {
        InitializeComponent();
    }

    internal void SetAssets(StandDirectionPoseAssets assets)
    {
        BodyBaseLayer.Source = assets.BodyBase;
        UpperBodyBaseLayer.Source = assets.UpperBodyBase;
        HeadBaseLayer.Source = assets.HeadBase;
        NeckFurLayer.Source = assets.NeckFur;
        ChestFurLayer.Source = assets.ChestFur;
        EarLLayer.Source = assets.EarL;
        EarRLayer.Source = assets.EarR;
        EyeBaseLayer.Source = assets.EyeBase;
        PupilLLayer.Source = assets.PupilL;
        PupilRLayer.Source = assets.PupilR;
        BlinkClosedLayer.Source = assets.BlinkClosed;

        ApplyEarLayout(EarLLayer, EarLRotate, assets.Ears[0]);
        ApplyEarLayout(EarRLayer, EarRRotate, assets.Ears[1]);
        if (assets.Eyes.Length >= 2)
        {
            eyeCenterX = (assets.Eyes[0].X + assets.Eyes[1].X) * .5;
            eyeCenterY = (assets.Eyes[0].Y + assets.Eyes[1].Y) * .5;
            ApplyEyeSocketClip(PupilLViewport, assets.Eyes[0]);
            ApplyEyeSocketClip(PupilRViewport, assets.Eyes[1]);
        }

        HeadRotateTransform.CenterX = assets.HeadPivot.X;
        HeadRotateTransform.CenterY = assets.HeadPivot.Y;
        UpperBodySkewTransform.CenterX = assets.UpperBodyPivot.X;
        UpperBodySkewTransform.CenterY = assets.UpperBodyPivot.Y;
    }

    internal void ApplyMotion(double eyeLX, double eyeLY, double eyeRX, double eyeRY,
        double headX, double headY, double headRotate,
        double upperX, double upperY, double upperSkew, double blink, double earL, double earR)
    {
        PupilLTransform.X = eyeLX;
        PupilLTransform.Y = eyeLY;
        PupilRTransform.X = eyeRX;
        PupilRTransform.Y = eyeRY;
        HeadTranslateTransform.X = headX;
        HeadTranslateTransform.Y = headY;
        HeadRotateTransform.Angle = headRotate;
        UpperBodyTranslateTransform.X = upperX;
        UpperBodyTranslateTransform.Y = upperY;
        UpperBodySkewTransform.AngleX = upperSkew;
        BlinkClosedLayer.Opacity = blink * transitionEyeWeight;
        EarLRotate.Angle = earL;
        EarRRotate.Angle = earR;
    }

    internal void ApplyTransitionWeights(double body, double upperBody, double chest,
        double head, double ear, double eye)
    {
        BodyBaseLayer.Opacity = body;
        UpperBodyBaseLayer.Opacity = upperBody;
        NeckFurLayer.Opacity = ChestFurLayer.Opacity = chest;
        HeadBaseLayer.Opacity = head;
        EarLLayer.Opacity = EarRLayer.Opacity = ear;
        EyeBaseLayer.Opacity = PupilLLayer.Opacity = PupilRLayer.Opacity = eye;
        transitionEyeWeight = eye;
    }

    internal void ResetTransitionWeights()
    {
        ApplyTransitionWeights(1, 1, 1, 1, 1, 1);
    }

    internal System.Windows.Point EyePositionIn(Visual ancestor)
    {
        return HeadGroup.TransformToAncestor(ancestor).Transform(
            new System.Windows.Point(eyeCenterX, eyeCenterY));
    }

    private static void ApplyEarLayout(System.Windows.Controls.Image image, RotateTransform transform,
        DirectionEarLayout ear)
    {
        System.Windows.Controls.Canvas.SetLeft(image, ear.X);
        System.Windows.Controls.Canvas.SetTop(image, ear.Y);
        image.Width = ear.Width;
        image.Height = ear.Height;
        transform.CenterX = ear.PivotX;
        transform.CenterY = ear.PivotY;
    }

    private static void ApplyEyeSocketClip(System.Windows.Controls.Canvas viewport, System.Windows.Point center)
    {
        viewport.Clip = new EllipseGeometry(center, 9, 7);
    }
}
