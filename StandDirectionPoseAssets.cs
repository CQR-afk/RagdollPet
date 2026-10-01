using System.Windows.Media.Imaging;

namespace RagdollPet;

internal sealed record DirectionEarLayout(double X, double Y, double Width, double Height,
    double PivotX, double PivotY);

internal sealed record StandDirectionPoseAssets(
    BitmapImage BodyBase,
    BitmapImage UpperBodyBase,
    BitmapImage HeadBase,
    BitmapImage NeckFur,
    BitmapImage ChestFur,
    BitmapImage EarL,
    BitmapImage EarR,
    BitmapImage EyeBase,
    BitmapImage PupilL,
    BitmapImage PupilR,
    BitmapImage BlinkClosed,
    System.Windows.Point HeadPivot,
    System.Windows.Point UpperBodyPivot,
    System.Windows.Point[] Eyes,
    DirectionEarLayout[] Ears);
