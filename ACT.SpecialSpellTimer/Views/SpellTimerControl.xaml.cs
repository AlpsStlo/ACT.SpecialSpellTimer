﻿using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

using ACT.SpecialSpellTimer.Config;
using ACT.SpecialSpellTimer.Image;
using ACT.SpecialSpellTimer.Utility;

namespace ACT.SpecialSpellTimer.Views
{
    /// <summary>
    /// SpellTimerControl
    /// </summary>
    public partial class SpellTimerControl : UserControl
    {
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public SpellTimerControl()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// バーの色
        /// </summary>
        public string BarColor { get; set; }

        /// <summary>
        /// バーの高さ
        /// </summary>
        public int BarHeight { get; set; }

        /// <summary>
        /// バーOutlineの色
        /// </summary>
        public string BarOutlineColor { get; set; }

        /// <summary>
        /// バーの幅
        /// </summary>
        public int BarWidth { get; set; }

        /// <summary>
        /// Should font color change when warning?
        /// </summary>
        public bool ChangeFontColorsWhenWarning { get; set; }

        /// <summary>
        /// Fontの色
        /// </summary>
        public string FontColor { get; set; }

        /// <summary>
        /// フォント
        /// </summary>
        public FontInfo FontInfo { get; set; }

        /// <summary>
        /// FontOutlineの色
        /// </summary>
        public string FontOutlineColor { get; set; }

        /// <summary>
        /// スペル名を非表示とするか？
        /// </summary>
        public bool HideSpellName { get; set; }

        /// <summary>
        /// プログレスバーを逆にするか？
        /// </summary>
        public bool IsReverse { get; set; }

        /// <summary>
        /// リキャストタイムを重ねて表示するか？
        /// </summary>
        public bool OverlapRecastTime { get; set; }

        /// <summary>
        /// リキャストの進捗率
        /// </summary>
        public double Progress { get; set; }

        /// <summary>
        /// 残りリキャストTime(秒数)
        /// </summary>
        public double RecastTime { get; set; }

        /// <summary>
        /// リキャスト中にアイコンの明度を下げるか？
        /// </summary>
        public bool ReduceIconBrightness { get; set; }

        /// <summary>
        /// スペルのIcon
        /// </summary>
        public string SpellIcon { get; set; }

        /// <summary>
        /// スペルIconサイズ
        /// </summary>
        public int SpellIconSize { get; set; }

        /// <summary>
        /// スペルのTitle
        /// </summary>
        public string SpellTitle { get; set; }

        /// <summary>
        /// スペル表示領域の幅
        /// </summary>
        public int SpellWidth =>
            this.BarWidth > this.SpellIconSize ?
            this.BarWidth :
            this.SpellIconSize;

        /// <summary>
        /// WarningFontの色
        /// </summary>
        public string WarningFontColor { get; set; }

        /// <summary>
        /// WarningFontOutlineの色
        /// </summary>
        public string WarningFontOutlineColor { get; set; }

        /// <summary>
        /// Time left warning in seconds
        /// </summary>
        public double WarningTime { get; set; }

        /// <summary>
        /// リキャスト秒数の書式
        /// </summary>
        private static string RecastTimeFormat =>
            Settings.Default.EnabledSpellTimerNoDecimal ? "N0" : "N1";

        /// <summary>バーのアニメーション用DoubleAnimation</summary>
        private DoubleAnimation BarAnimation { get; set; }

        /// <summary>バーの背景のBrush</summary>
        private SolidColorBrush BarBackBrush { get; set; }

        /// <summary>バーのBrush</summary>
        private SolidColorBrush BarBrush { get; set; }

        /// <summary>バーのアウトラインのBrush</summary>
        private SolidColorBrush BarOutlineBrush { get; set; }

        /// <summary>フォントのBrush</summary>
        private SolidColorBrush FontBrush { get; set; }

        /// <summary>フォントのアウトラインBrush</summary>
        private SolidColorBrush FontOutlineBrush { get; set; }

        /// <summary>フォントのBrush</summary>
        private SolidColorBrush WarningFontBrush { get; set; }

        /// <summary>フォントのアウトラインBrush</summary>
        private SolidColorBrush WarningFontOutlineBrush { get; set; }

        /// <summary>
        /// 描画を更新する
        /// </summary>
        public void Refresh()
        {
            // アイコンの不透明度を設定する
            var opacity = 1.0;
            if (this.ReduceIconBrightness)
            {
                if (this.RecastTime > 0)
                {
                    opacity = this.IsReverse ?
                        1.0 :
                        ((double)Settings.Default.ReduceIconBrightness / 100d);
                }
                else
                {
                    opacity = this.IsReverse ?
                        ((double)Settings.Default.ReduceIconBrightness / 100d) :
                        1.0;
                }
            }

            if (this.SpellIconImage.Opacity != opacity)
            {
                this.SpellIconImage.Opacity = opacity;
            }

            // リキャスト時間を描画する
            var tb = this.RecastTimeTextBlock;
            var recast = this.RecastTime > 0 ?
                this.RecastTime.ToString(RecastTimeFormat) :
                this.IsReverse ? Settings.Default.OverText : Settings.Default.ReadyText;

            if (tb.Text != recast) tb.Text = recast;
            tb.SetFontInfo(this.FontInfo);
            tb.SetAutoStrokeThickness();

            var fill = this.FontBrush;
            var stroke = this.FontOutlineBrush;

            if (this.ChangeFontColorsWhenWarning &&
                this.RecastTime < this.WarningTime)
            {
                fill = this.WarningFontBrush;
                stroke = this.WarningFontOutlineBrush;
            }

            if (tb.Fill != fill) tb.Fill = fill;
            if (tb.Stroke != stroke) tb.Stroke = stroke;
        }

        /// <summary>
        /// バーのアニメーションを開始する
        /// </summary>
        public void StartBarAnimation()
        {
            if (this.BarWidth == 0)
            {
                return;
            }

            if (this.BarAnimation == null)
            {
                this.BarAnimation = new DoubleAnimation();
                this.BarAnimation.AutoReverse = false;
            }

            var fps = (int)Math.Ceiling(this.BarWidth / this.RecastTime);
            if (fps <= 0 || fps > Settings.Default.MaxFPS)
            {
                fps = Settings.Default.MaxFPS;
            }

            Timeline.SetDesiredFrameRate(this.BarAnimation, fps);

            var currentWidth = this.IsReverse ?
                (double)(this.BarWidth * (1.0d - this.Progress)) :
                (double)(this.BarWidth * this.Progress);
            if (this.IsReverse)
            {
                this.BarAnimation.From = currentWidth / this.BarWidth;
                this.BarAnimation.To = 0;
            }
            else
            {
                this.BarAnimation.From = currentWidth / this.BarWidth;
                this.BarAnimation.To = 1.0;
            }

            this.BarAnimation.Duration = new Duration(TimeSpan.FromSeconds(this.RecastTime));

            this.BarScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            this.BarScale.BeginAnimation(ScaleTransform.ScaleXProperty, this.BarAnimation);
        }

        /// <summary>
        /// 描画設定を更新する
        /// </summary>
        public void Update()
        {
            this.Width = this.SpellWidth;

            // Brushを生成する
            var fontColor = string.IsNullOrWhiteSpace(this.FontColor) ?
                Settings.Default.FontColor.ToWPF() :
                this.FontColor.FromHTMLWPF();
            var fontOutlineColor = string.IsNullOrWhiteSpace(this.FontOutlineColor) ?
                Settings.Default.FontOutlineColor.ToWPF() :
                this.FontOutlineColor.FromHTMLWPF();
            var warningFontColor = string.IsNullOrWhiteSpace(this.WarningFontColor) ?
                Settings.Default.WarningFontColor.ToWPF() :
                this.WarningFontColor.FromHTMLWPF();
            var warningFontOutlineColor = string.IsNullOrWhiteSpace(this.WarningFontOutlineColor) ?
                Settings.Default.WarningFontOutlineColor.ToWPF() :
                this.WarningFontOutlineColor.FromHTMLWPF();

            var barColor = string.IsNullOrWhiteSpace(this.BarColor) ?
                Settings.Default.ProgressBarColor.ToWPF() :
                this.BarColor.FromHTMLWPF();
            var barBackColor = barColor.ChangeBrightness(0.4d);
            var barOutlineColor = string.IsNullOrWhiteSpace(this.BarOutlineColor) ?
                Settings.Default.ProgressBarOutlineColor.ToWPF() :
                this.BarOutlineColor.FromHTMLWPF();

            this.FontBrush = this.GetBrush(fontColor);
            this.FontOutlineBrush = this.GetBrush(fontOutlineColor);
            this.WarningFontBrush = this.GetBrush(warningFontColor);
            this.WarningFontOutlineBrush = this.GetBrush(warningFontOutlineColor);
            this.BarBrush = this.GetBrush(barColor);
            this.BarBackBrush = this.GetBrush(barBackColor);
            this.BarOutlineBrush = this.GetBrush(barOutlineColor);

            var tb = default(OutlineTextBlock);
            var font = this.FontInfo;

            // アイコンを描画する
            var image = this.SpellIconImage;
            var iconFile = IconController.Instance.GetIconFile(this.SpellIcon);
            if (image.Source == null &&
                iconFile != null)
            {
                var bitmap = new BitmapImage(new Uri(iconFile.FullPath));
                image.Source = bitmap;
                image.Height = this.SpellIconSize;
                image.Width = this.SpellIconSize;

                this.SpellIconPanel.OpacityMask = new ImageBrush(bitmap);
            }

            // Titleを描画する
            tb = this.SpellTitleTextBlock;
            var title = string.IsNullOrWhiteSpace(this.SpellTitle) ? "　" : this.SpellTitle;
            title = title.Replace(",", Environment.NewLine);

            if (tb.Text != title) tb.Text = title;
            if (tb.Fill != this.FontBrush) tb.Fill = this.FontBrush;
            if (tb.Stroke != this.FontOutlineBrush) tb.Stroke = this.FontOutlineBrush;
            tb.SetFontInfo(font);
            tb.SetAutoStrokeThickness();

            if (this.HideSpellName)
            {
                tb.Visibility = Visibility.Collapsed;
            }

            if (this.OverlapRecastTime)
            {
                this.RecastTimePanel.SetValue(Grid.ColumnProperty, 0);
                this.RecastTimePanel.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
                this.RecastTimePanel.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
                this.RecastTimePanel.Width = this.SpellIconSize >= 6 ? this.SpellIconSize - 6 : double.NaN;
                this.RecastTimePanel.Height = this.RecastTimePanel.Width;
            }
            else
            {
                this.RecastTimePanel.Width = double.NaN;
                this.RecastTimePanel.Height = double.NaN;
            }

            // ProgressBarを描画する
            var foreRect = this.BarRectangle;
            if (foreRect.Fill != this.BarBrush) foreRect.Fill = this.BarBrush;
            if (foreRect.Width != this.BarWidth) foreRect.Width = this.BarWidth;
            if (foreRect.Height != this.BarHeight) foreRect.Height = this.BarHeight;

            var backRect = this.BarBackRectangle;
            if (backRect.Fill != this.BarBackBrush) backRect.Fill = this.BarBackBrush;
            if (backRect.Width != this.BarWidth) backRect.Width = this.BarWidth;

            var outlineRect = this.BarOutlineRectangle;
            if (outlineRect.Stroke != this.BarOutlineBrush) outlineRect.Stroke = this.BarOutlineBrush;

            // バーのエフェクトの色を設定する
            var barEffectColor = this.BarBrush.Color.ChangeBrightness(1.05d);
            if (this.BarEffect.Color != barEffectColor) this.BarEffect.Color = barEffectColor;
        }
    }
}
