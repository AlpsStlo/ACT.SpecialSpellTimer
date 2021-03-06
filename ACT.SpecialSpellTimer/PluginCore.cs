﻿using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ACT.SpecialSpellTimer.Config;
using ACT.SpecialSpellTimer.Forms;
using ACT.SpecialSpellTimer.Models;
using ACT.SpecialSpellTimer.Utility;
using Advanced_Combat_Tracker;

namespace ACT.SpecialSpellTimer
{
    /// <summary>
    /// PluginCore
    /// </summary>
    public class PluginCore
    {
        #region Singleton

        private static PluginCore instance;

        public static PluginCore Instance => instance;

        public static void Initialize(
            Plugin plugin)
        {
            instance = new PluginCore();
            instance.PluginRoot = plugin;
        }

        #endregion Singleton

        /// <summary>
        /// 設定パネル
        /// </summary>
        public ConfigPanel ConfigPanel { get; private set; }

        /// <summary>
        /// 自身の場所
        /// </summary>
        public string Location { get; private set; }

        public Plugin PluginRoot { get; private set; }

        /// <summary>
        /// プラグインステータス表示ラベル
        /// </summary>
        private Label PluginStatusLabel { get; set; }

        /// <summary>
        /// 表示切り替えボタン
        /// </summary>
        private CheckBox SwitchVisibleButton { get; set; }

        /// <summary>
        /// 後片付けをする
        /// </summary>
        public void DeInitPluginCore()
        {
            try
            {
                PluginMainWorker.Instance.End();

                this.RemoveSwitchVisibleButton();
                this.PluginStatusLabel.Text = "Plugin Exited";

                // 設定ファイルを保存する
                Settings.Default.Save();

                Logger.Write("Plugin Exited.");
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(
                    ex,
                    "Plugin deinit error.");

                Logger.Write("Plugin deinit error.", ex);

                this.PluginStatusLabel.Text = "Plugin Exited Error";
            }
        }

        /// <summary>
        /// 初期化する
        /// </summary>
        /// <param name="pluginScreenSpace">Pluginタブ</param>
        /// <param name="pluginStatusText">Pluginステータスラベル</param>
        public void InitPluginCore(
            TabPage pluginScreenSpace,
            Label pluginStatusText)
        {
            try
            {
                Logger.Write("Plugin Start.");

                // 設定ファイルを読み込む
                Settings.Default.Load();
                Settings.Default.ApplyRenderMode();

                // WPFアプリケーションを開始する
                if (System.Windows.Application.Current == null)
                {
                    new System.Windows.Application();
                    System.Windows.Application.Current.ShutdownMode =
                        System.Windows.ShutdownMode.OnExplicitShutdown;
                }

                pluginScreenSpace.Text = Translate.Get("LN_Tabname");
                this.PluginStatusLabel = pluginStatusText;

                // アップデートを確認する
                Task.Run(() =>
                {
                    this.Update();
                });

                // 自身の場所を格納しておく
                var plugin = ActGlobals.oFormActMain.PluginGetSelfData(this.PluginRoot);
                if (plugin != null)
                {
                    this.Location = plugin.pluginFile.DirectoryName;
                }

                // 設定Panelを追加する
                this.ConfigPanel = new ConfigPanel();
                pluginScreenSpace.Controls.Add(this.ConfigPanel);
                this.ConfigPanel.Dock = DockStyle.Fill;

                // 設定ファイルのバックアップを作成する
                SpellTimerTable.Instance.Backup();
                OnePointTelopTable.Instance.Backup();
                PanelTable.Instance.Backup();

                // 設定ファイルを読み込む
                SpellTimerTable.Instance.Load();
                OnePointTelopTable.Instance.Load();
                PanelTable.Instance.Load();

                // 本体を開始する
                PluginMainWorker.Instance.Begin();

                this.SetSwitchVisibleButton();
                this.PluginStatusLabel.Text = "Plugin Started";

                Logger.Write("Plugin Started.");
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(
                    ex,
                    "Plugin init error.");

                Logger.Write("Plugin init error.", ex);

                this.PluginStatusLabel.Text = "Plugin Initialize Error";
            }
        }

        #region SpeSpeButton

        /// <summary>
        /// 表示切り替えボタン（スペスペボタン）の状態を切り替える
        /// </summary>
        /// <param name="visible">
        /// 切り替える状態</param>
        public async void ChangeSwitchVisibleButton(
            bool visible)
        {
            await Task.Run(() =>
            {
                this.SwitchOverlay(visible);
            });

            ActInvoker.Invoke(() =>
            {
                this.ChangeButtonColor();
            });
        }

        private void ChangeButtonColor()
        {
            var button = this.SwitchVisibleButton;

            if (Settings.Default.OverlayVisible)
            {
                button.BackColor = Color.SandyBrown;
                button.ForeColor = Color.WhiteSmoke;
            }
            else
            {
                button.BackColor = SystemColors.Control;
                button.ForeColor = Color.Black;
            }
        }

        /// <summary>
        /// 表示切り替えボタンを除去する
        /// </summary>
        private void RemoveSwitchVisibleButton()
        {
            if (this.SwitchVisibleButton != null)
            {
                ActGlobals.oFormActMain.Controls.Remove(this.SwitchVisibleButton);

                this.SwitchVisibleButton.Dispose();
                this.SwitchVisibleButton = null;
            }
        }

        private void ReplaceButton()
        {
            if (this.SwitchVisibleButton != null &&
                !this.SwitchVisibleButton.IsDisposed &&
                this.SwitchVisibleButton.IsHandleCreated)
            {
                var leftButton = (
                    from Control x in ActGlobals.oFormActMain.Controls
                    where
                    !x.Equals(this.SwitchVisibleButton) &&
                    (
                        x is Button ||
                        x is CheckBox
                    )
                    orderby
                    x.Left
                    select
                    x).FirstOrDefault();

                var location = leftButton != null ?
                    new Point(leftButton.Left - this.SwitchVisibleButton.Width - 1, 0) :
                    new Point(ActGlobals.oFormActMain.Width - 533, 0);

                ActInvoker.Invoke(() =>
                {
                    this.SwitchVisibleButton.Location = location;
                });
            }
        }

        /// <summary>
        /// 表示切り替えボタンを配置する
        /// </summary>
        private void SetSwitchVisibleButton()
        {
            this.SwitchVisibleButton = new CheckBox()
            {
                Name = "SpecialSpellTimerSwitchVisibleButton",
                Text = Translate.Get("SupeSupe"),
                TextAlign = ContentAlignment.MiddleCenter,
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(ActGlobals.oFormActMain.Width - 533, 0),
                AutoSize = true,
            };

            this.SwitchVisibleButton.CheckedChanged += async (s, e) =>
            {
                await Task.Run(() =>
                {
                    this.SwitchOverlay(!Settings.Default.OverlayVisible);
                });

                this.ChangeButtonColor();
                Application.DoEvents();
            };

            this.ChangeButtonColor();

            ActGlobals.oFormActMain.Resize += (s, e) => this.ReplaceButton();
            ActGlobals.oFormActMain.Controls.Add(this.SwitchVisibleButton);
            ActGlobals.oFormActMain.Controls.SetChildIndex(this.SwitchVisibleButton, 1);

            Task.Run(async () =>
            {
                this.ReplaceButton();

                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    this.ReplaceButton();
                }
            });
        }

        private void SwitchOverlay(
            bool visibility)
        {
            Settings.Default.OverlayVisible = visibility;
            Settings.Default.Save();

            SpellsController.Instance.ClosePanels();
            TickersController.Instance.CloseTelops();

            TableCompiler.Instance.RefreshPlayerPlacceholder();
            TableCompiler.Instance.RefreshPartyPlaceholders();
            TableCompiler.Instance.RefreshPetPlaceholder();
            TableCompiler.Instance.RecompileSpells();
            TableCompiler.Instance.RecompileTickers();
        }

        #endregion SpeSpeButton

        /// <summary>
        /// アップデートを行う
        /// </summary>
        private void Update()
        {
            if (Settings.Default.UpdateCheckInterval >= 0.0d)
            {
                if ((DateTime.Now - Settings.Default.LastUpdateDateTime).TotalHours >=
                    Settings.Default.UpdateCheckInterval)
                {
                    var message = UpdateChecker.Update();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        Logger.Write(message);
                    }

                    Settings.Default.LastUpdateDateTime = DateTime.Now;
                    Settings.Default.Save();
                }
            }
        }
    }
}
