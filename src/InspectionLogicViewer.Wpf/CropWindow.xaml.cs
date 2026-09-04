using System.Windows;
using System.Windows.Media.Imaging;

namespace InspectionLogicViewer.Wpf;

public partial class CropWindow : Window
{
    public CropWindow(BitmapSource cropped)
    {
        InitializeComponent();
        CropImage.Source = cropped;
    }
}