using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BloxManager.Services;
using BloxManager.ViewModels;
using BloxManager.Views;
using BloxManager.Logging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;

namespace BloxManager
{
    public partial class App : Application
    {
        private static IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) => {
                var ex = args.ExceptionObject as Exception;
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatal_startup_error.txt"), ex?.ToString());
                MessageBox.Show(ex?.Message, "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            try 
            {
                _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IAccountService, AccountService>();
                    services.AddSingleton<ISettingsService, SettingsService>();
                    services.AddSingleton<IEncryptionService, EncryptionService>();
                    services.AddSingleton<IGameService, GameService>();
                    services.AddSingleton<IWebApiService, WebApiService>();
                    services.AddSingleton<IUpdateService, UpdateService>();
                    services.AddSingleton<IRobloxService, RobloxService>();
                    services.AddSingleton<IBrowserService, BrowserService>();
                    
                    services.AddSingleton<MainViewModel>();
                    services.AddTransient<AccountViewModel>();
                    services.AddTransient<AddAccountViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<BulkImportViewModel>();
                    
                    services.AddSingleton<MainWindow>();
                    services.AddTransient<AddAccountWindow>();
                    services.AddTransient<AccountDetailsWindow>();
                    services.AddTransient<BulkImportWindow>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .Build();

                _host.Start();

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatal_startup_error.txt"), ex.ToString());
                MessageBox.Show(ex.Message, "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }

        public static T GetService<T>() where T : class
        {
            if (_host?.Services.GetService(typeof(T)) is T service)
            {
                return service;
            }
            throw new InvalidOperationException($"Service of type {typeof(T).Name} not found.");
        }
    }
}
