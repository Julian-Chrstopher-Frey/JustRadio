namespace RadioBloom.Maui.Resources.Styles;

public partial class AppStyles : ResourceDictionary
{
	public AppStyles()
	{
		MergedDictionaries.Add(new AppColors());
		InitializeComponent();
	}
}
