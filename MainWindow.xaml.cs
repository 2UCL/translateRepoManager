using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using ValveKeyValue;

namespace translateRepoManager2
{
    public partial class MainWindow : Window
    {
        private const string
            TITLE = "R.E.P.O. 日本語パッチインストーラ",
            LABEL_MAIN_OFFLINE = "接続できません",
            LABEL_MAIN_CHECKING = "確認中...",
            LABEL_MAIN_READY = "インストールできます",
            LABEL_MAIN_PROGRESS = "インストール中...",
            LABEL_MAIN_DONE = "インストール完了",
            LABEL_MAIN_FAIL = "インストール失敗",
            LABEL_MAIN_UPDATE = "アップデートがあります",
            LABEL_MAIN_REMOVE_PROGRESS = "アンインストール中...",
            LABEL_MAIN_REMOVE_DONE = "アンインストール完了",
            LABEL_MAIN_REMOVE_FAIL = "アンインストール失敗",
            LABEL_MAIN_LATEST = "最新版をご利用です",
            LABEL_MAIN_NO_ADMIN = "管理者権限が必要です",
            LABEL_MAIN_NO_BASE = "R.E.P.O. が見つかりません",
            LABEL_SUB_OFFLINE = "インターネットに接続できるか確認してください",
            LABEL_SUB_READY = "準備完了",
            LABEL_SUB_NO_ADMIN = "アプリケーションを 管理者として起動 しなおしてください",
            LABEL_SUB_NO_BASE = "インストール先を確認してください",
            LABEL_SUB_DONE = "閉じるを押してインストーラを終了します",
            LABEL_SUB_DOWNLOAD0 = "受信します: {0}",
            LABEL_SUB_DOWNLOAD1 = "受信しています: {0} ({1}B)",
            LABEL_SUB_DOWNLOAD2 = "受信しています: {0} ({1}/{2}B)",
            LABEL_SUB_EXTRACT = "展開しています: {0}",
            LABEL_SUB_REMOVE = "削除しています: {0}",
            BUTTON_INSTALL = "インストール",
            BUTTON_UPDATE = "アップデート",
            BUTTON_RETRY = "再試行",
            BUTTON_REMOVE = "アンインストール",
            BUTTON_CLOSE = "閉じる";

        private const int
            PROGRESS_RESET = 0,
            PROGRESS_HAS_VALUE = 1,
            PROGRESS_NO_VALUE = 2,
            PROGRESS_DONE = 3,
            PROGRESS_PEND = 4,
            PROGRESS_DISABLE = -1,
            PROGRESS_ERROR = -2;

        private static readonly string? basePath = GetDirectoryAsync().Result;
        private readonly string statusPath = Path.Combine(basePath, "STATUS");

        private readonly Encoding encode = Encoding.UTF8;

        private const string
            githubUrl = "https://github.com/2UCL/translateREPO/releases/latest",
            tagPrefix = "/2UCL/translateREPO/releases/tag/",
            targetPath = "/steamapps/common/REPO/REPO_Data/StreamingAssets/Localizations/";
        private readonly string DownloadUrlTemplate = "https://github.com/2UCL/translateREPO/releases/download/{0}/ja.zip";
        private string? latestTag;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = TITLE;

            nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId mainWindowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(mainWindowId);

            appWindow.Resize(new(600, 500));

            OverlappedPresenter? presenter = appWindow.Presenter as OverlappedPresenter;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        private void Page_Loaded(object sender, RoutedEventArgs args)
        {
            // 管理者権限
            if (!IsAdministrator())
            {
                try
                {
                    // UAC
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath,
                        UseShellExecute = true,
                        Verb = "RunAs",
                    });
                    Environment.Exit(0);
                }
                catch (Exception e)
                {
                    // 標準ユーザ
                    Log.Error($"AdministratorException: {e.Message}");
                    SetLabelText(LABEL_MAIN_NO_ADMIN, LABEL_SUB_NO_ADMIN);
                    SetButtonText(BUTTON_CLOSE, null, null);
                    SetButtonStat(true, false, false);
                    return;
                }
            }

            GetUpdate();
        }

        private void SetLabelText(string? main = null, string? sub = null)
        {
            mainLabel.Text = main ?? mainLabel.Text;
            subLabel.Text = sub ?? subLabel.Text;
        }
        private void SetButtonText(string? primary, string? option1, string? option2)
        {
            primaryButton.Content = primary ?? primaryButton.Content;
            optionButton1.Content = option1 ?? optionButton1.Content;
            optionButton2.Content = option2 ?? optionButton2.Content;
        }

        private void SetButtonStat(Boolean all)
        {
            SetButtonStat(all, all, all);
        }
        private void SetButtonStat(Boolean primary, Boolean option1, Boolean option2)
        {
            primaryButton.IsEnabled = primary;
            optionButton1.IsEnabled = option1;
            optionButton2.IsEnabled = option2;
        }

        private void SetProgressMode(int mode = PROGRESS_RESET)
        {
            progressBar.ShowPaused = mode == PROGRESS_PEND;
            progressBar.ShowError = mode == PROGRESS_ERROR;

            switch (mode)
            {
                case PROGRESS_RESET:
                    // reset
                    SetProgressStat(0, 1);
                    progressBar.IsEnabled = true;
                    progressBar.IsIndeterminate = false;
                    break;
                case PROGRESS_HAS_VALUE:
                case PROGRESS_PEND:
                    // determinate, pend
                    progressBar.IsEnabled = true;
                    progressBar.IsIndeterminate = false;
                    break;
                case PROGRESS_NO_VALUE:
                    // indeterminate
                    progressBar.IsEnabled = true;
                    progressBar.IsIndeterminate = true;
                    break;
                case PROGRESS_DONE:
                case PROGRESS_ERROR:
                    // done, error
                    SetProgressStat(1, 1);
                    progressBar.IsEnabled = true;
                    progressBar.IsIndeterminate = false;
                    break;
                case PROGRESS_DISABLE:
                    // disable
                    progressBar.IsEnabled = false;
                    break;
            }
        }
        private void SetProgressStat(int value, int max = -1)
        {
            progressBar.Value = value;
            if (max != -1) progressBar.Maximum = max;
        }
        private void SetProgressStat(long? value, long? max = -1)
        {
            progressBar.Value = (double)value;
            if (max != -1) progressBar.Maximum = (double)max;
        }

        private static bool IsAdministrator()
        {
            WindowsPrincipal principal = new(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private async void GetUpdate()
        {
            SetLabelText(LABEL_MAIN_CHECKING, "");
            SetProgressMode(PROGRESS_NO_VALUE);
            try
            {
                // 最新のリリースタグを取得
                latestTag = await GetLatestReleaseTag();
                if (string.IsNullOrEmpty(latestTag))
                {
                    // タグ取得不可
                    Log.Error($"Failed to get latest tag.");
                    SetLabelText(LABEL_MAIN_OFFLINE, LABEL_SUB_OFFLINE);
                    SetButtonText(BUTTON_CLOSE, BUTTON_RETRY, null);
                    SetButtonStat(true, true, false);
                    SetProgressMode(PROGRESS_ERROR);

                    return;
                }
                if (!Directory.Exists(basePath))
                {
                    // ゲーム未インストール
                    Log.Error($"Base game not found.");
                    SetLabelText(LABEL_MAIN_NO_BASE, LABEL_SUB_NO_BASE);
                    SetButtonText(BUTTON_CLOSE, null, null);
                    SetButtonStat(true, false, false);
                    SetProgressMode();
                    return;
                }
                if (File.Exists(statusPath))
                {
                    try
                    {
                        StreamReader sr = new(statusPath, encoding: encode);
                        string? currentVersion = sr.ReadLine();
                        sr.Close();

                        Log.Info($"[GET] Success! Current:{currentVersion}, Latest:{latestTag}");
                        if (currentVersion != null)
                        {
                            if (currentVersion == latestTag)
                            {
                                // 最新版
                                SetLabelText(LABEL_MAIN_LATEST, LABEL_SUB_READY);
                                SetButtonText(BUTTON_CLOSE, BUTTON_RETRY, BUTTON_REMOVE);
                                SetButtonStat(true);
                                SetProgressMode();
                            }
                            else
                            {
                                // アップデートあり
                                SetLabelText(LABEL_MAIN_UPDATE, LABEL_SUB_READY);
                                SetButtonText(BUTTON_UPDATE, BUTTON_RETRY, BUTTON_REMOVE);
                                SetButtonStat(true);
                                SetProgressMode();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // バージョン不明
                        Log.Error($"StatusException: {e.Message}");
                        SetLabelText(LABEL_MAIN_READY, LABEL_SUB_READY);
                        SetButtonText(BUTTON_INSTALL, BUTTON_RETRY, BUTTON_REMOVE);
                        SetButtonStat(true, true, false);
                        SetProgressMode();
                    }
                }
                else
                {
                    // パッチ未インストール
                    Log.Info($"[GET] Success! Current:N/A, Latest:{latestTag}");
                    SetLabelText(LABEL_MAIN_READY, LABEL_SUB_READY);
                    SetButtonText(BUTTON_INSTALL, BUTTON_RETRY, BUTTON_REMOVE);
                    SetButtonStat(true, true, false);
                    SetProgressMode();
                }
            }
            catch (Exception e)
            {
                // オフラインなど
                Log.Error($"GetException: {e.Message}");
                SetLabelText(LABEL_MAIN_OFFLINE, e.Message);
                SetButtonText(BUTTON_CLOSE, BUTTON_RETRY, null);
                SetButtonStat(true, true, false);
                SetProgressMode(PROGRESS_ERROR);
                return;
            }
        }

        private async void Install()
        {
            SetLabelText(LABEL_MAIN_PROGRESS);
            SetProgressMode(PROGRESS_NO_VALUE);

            // cleanup
            Uninstall(true);

            string downloadUrl = string.Format(DownloadUrlTemplate, latestTag);
            string zipFilePath = Path.Combine(Path.GetTempPath(), $"{latestTag}.zip");

            SetLabelText(sub: string.Format(LABEL_SUB_DOWNLOAD0, latestTag));

            // download
            try
            {
                using HttpClient client = new();
                using HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                using Stream contentStream = await response.Content.ReadAsStreamAsync(),
                    fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                byte[] buffer = new byte[4096];
                int bytesRead;
                long currentBytes = 0;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    currentBytes += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        Log.Info($"[RECEIVE] Target: {latestTag} | {currentBytes} / {totalBytes} Byte(s)");
                        SetLabelText(sub: string.Format(LABEL_SUB_DOWNLOAD2, latestTag, currentBytes, totalBytes));
                        SetProgressMode(PROGRESS_HAS_VALUE);
                        SetProgressStat(currentBytes, totalBytes);
                    }
                    else
                    {
                        Log.Info($"[RECEIVE] Target: {latestTag} | {currentBytes} Byte(s)");
                        SetLabelText(sub: string.Format(LABEL_SUB_DOWNLOAD1, latestTag, currentBytes));
                        SetProgressMode(PROGRESS_NO_VALUE);
                    }
                }

            }
            catch (Exception e)
            {
                // download fail
                Log.Error($"DownloadException: {e.Message}");
                SetLabelText(LABEL_MAIN_FAIL, e.Message);
                SetButtonText(BUTTON_CLOSE, BUTTON_RETRY, null);
                SetButtonStat(true, true, false);
                SetProgressMode(PROGRESS_ERROR);
                return;
            }

            //extract & status update
            SetProgressMode(PROGRESS_NO_VALUE);
            try
            {
                if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath);
                Log.Info($"[WRITE] Extract From:{zipFilePath}, To:{basePath}");

                SetLabelText(sub: string.Format(LABEL_SUB_EXTRACT, basePath));
                SetProgressMode(PROGRESS_HAS_VALUE);

                using ZipArchive archive = ZipFile.OpenRead(zipFilePath);
                long totalEntries = archive.Entries.Count;
                long currentEntry = 0;

                // path manage
                StreamWriter sw = new(statusPath, false, encode);
                sw.WriteLine(latestTag);

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destinationPath = Path.Combine(basePath, entry.FullName);
                    SetLabelText(sub: string.Format(LABEL_SUB_EXTRACT, destinationPath));
                    Log.Info($"[WRITE] Extracting {destinationPath} ...");
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // on directory
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        // on file
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        entry.ExtractToFile(destinationPath, true);
                    }
                    // ドライブ変更に備えて相対パス
                    sw.WriteLine(entry.FullName);

                    SetProgressStat(++currentEntry, totalEntries);
                }

                sw.Close();
            }
            catch (Exception e)
            {
                Log.Error($"ExtractException: {e.Message}");
                try
                {
                    // インストールに失敗しているので可能なら状態をクリア
                    File.Delete(statusPath);
                }
                catch (Exception) { }

                SetLabelText(LABEL_MAIN_FAIL, e.Message);
                SetButtonText(BUTTON_CLOSE, BUTTON_RETRY, null);
                SetButtonStat(true, true, false);
                SetProgressMode(PROGRESS_ERROR);
                return;
            }

            Log.Info($"[WRITE] Done!");
            Log.Info($"Install success!");
            SetLabelText(LABEL_MAIN_DONE, LABEL_SUB_DONE);
            SetButtonText(BUTTON_CLOSE, null, null);
            SetButtonStat(true, false, false);
            SetProgressMode(PROGRESS_DONE);
        }

        private async void Uninstall(bool silent = false)
        {
            if (silent)
            {
                if (!File.Exists(statusPath)) return;
            }
            else
            {
                SetLabelText(LABEL_MAIN_REMOVE_PROGRESS);
                SetProgressMode(PROGRESS_NO_VALUE);
            }

            try
            {
                // 1行目はバージョンを格納しているため無視
                StreamReader sr = new(statusPath, encode);
                sr.ReadLine();

                while (!sr.EndOfStream)
                {
                    string originPath = Path.Combine(basePath, sr.ReadLine());
                    SetLabelText(sub: string.Format(LABEL_SUB_REMOVE, originPath));
                    Log.Info($"[REMOVE] {originPath}");
                    File.Delete(originPath);
                }
                sr.Close();

                File.Delete(statusPath);
            }
            catch (Exception e)
            {
                Log.Error($"UninstallException: {e.Message}");

                try
                {
                    // アンインストールに失敗しているが可能なら状態をクリア
                    File.Delete(statusPath);
                }
                catch (Exception) { }
                if (!silent)
                {
                    SetLabelText(LABEL_MAIN_REMOVE_FAIL, e.Message);
                    SetButtonText(BUTTON_CLOSE, BUTTON_RETRY, null);
                    SetButtonStat(true, true, false);
                    SetProgressMode(PROGRESS_ERROR);
                }
                return;
            }

            Log.Info($"[REMOVE] Done!");
            if (silent) return;
            Log.Info($"Uninstall success!");
            SetLabelText(LABEL_MAIN_REMOVE_DONE, LABEL_SUB_DONE);
            SetButtonText(BUTTON_CLOSE, null, null);
            SetButtonStat(true, false, false);
            SetProgressMode(PROGRESS_DONE);
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            SetButtonStat(false);
            switch (((Button)sender).Content)
            {
                case BUTTON_INSTALL:
                case BUTTON_UPDATE:
                    Install();
                    break;
                case BUTTON_CLOSE:
                    Environment.Exit(0);
                    break;
            }
        }

        private void OptionButton1_Click(object sender, RoutedEventArgs e)
        {
            SetButtonStat(false);
            switch (((Button)sender).Content)
            {
                case BUTTON_RETRY:
                    GetUpdate();
                    break;
            }
        }

        private void OptionButton2_Click(object sender, RoutedEventArgs e)
        {
            SetButtonStat(false);
            switch (((Button)sender).Content)
            {
                case BUTTON_REMOVE:
                    Uninstall();
                    break;
            }
        }

        public static async Task<string?> GetLatestReleaseTag()
        {
            // 最新のタグ名を取得
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "C# Application");
            HttpResponseMessage response = await client.GetAsync(githubUrl);
            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                int tagStartIndex = responseBody.IndexOf(tagPrefix) + tagPrefix.Length;
                int tagEndIndex = responseBody.IndexOf("\"", tagStartIndex);
                return tagStartIndex > tagPrefix.Length && tagEndIndex > tagStartIndex ?
                    responseBody[tagStartIndex..tagEndIndex] : null;
            }
            return null;
        }

        public static async Task<string?> GetDirectoryAsync()
        {
            string? steamPath = null;
            try
            {
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                if (key != null)
                {
                    steamPath = key.GetValue("InstallPath") as string;
                }

                string? gamePath = SteamLibrary.GetLibraryFolders(steamPath);


                if (!string.IsNullOrEmpty(gamePath))
                {
                    return gamePath + targetPath;
                }
                else
                {
                    return null;
                }
            }
            catch (FileNotFoundException e)
            {
                Log.Error($"GetDirectoryFileNotFoundException: {e.Message}");
                return null;

            }
            catch (Exception e)
            {
                Log.Error($"GetDirectoryException: {e.Message}");
                return null;
            }
        }
    }

    public class SteamLibrary
    {
        public static string? GetLibraryFolders(string steamPath)
        {
            string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFoldersPath))
            {
                try
                {
                    var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
                    using var stream = File.OpenRead(libraryFoldersPath);
                    var data = kv.Deserialize(stream);

                    foreach (var folder in data)
                    {
                        string path = (string)folder["path"];
                        if (File.Exists(Path.Combine(path, "steamapps", "appmanifest_3241660.acf")))
                        {
                            return path;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"SteamGetLibraryException: {e.Message}");
                }
            }
            return null;
        }
    }

    public class Log
    {
        public static void Info(string context) => Console.WriteLine(context);

        public static void Error(string context) => Console.Error.WriteLine(context);
    }
}
