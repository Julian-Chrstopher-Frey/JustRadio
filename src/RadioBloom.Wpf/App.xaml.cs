using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace RadioBloom.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                Shutdown(RunSelfTest());
                return;
            }

            MainWindow window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        private static int RunSelfTest()
        {
            try
            {
                LocationProfile location = StationCatalogService.GetApproximateLocation();
                var stations = StationCatalogService.LoadStations(location);
                if (stations == null || stations.Count < 4)
                {
                    return 20;
                }

                RadioStation station = stations.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.StreamUrl));
                if (station == null)
                {
                    return 21;
                }

                using (NativeAudioPlayer player = new NativeAudioPlayer())
                {
                    player.Open(station.StreamUrl);
                    player.Play();

                    NativePlayerState lastState = NativePlayerState.Ready;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(8);
                    while (DateTime.UtcNow < deadline)
                    {
                        lastState = player.GetState();
                        if (lastState == NativePlayerState.Live || lastState == NativePlayerState.Connecting)
                        {
                            return 0;
                        }

                        Thread.Sleep(400);
                    }

                    return lastState == NativePlayerState.Live || lastState == NativePlayerState.Connecting ? 0 : 30;
                }
            }
            catch
            {
                return 99;
            }
        }
    }
}
