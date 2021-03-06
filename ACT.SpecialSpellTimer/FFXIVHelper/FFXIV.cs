﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

using ACT.SpecialSpellTimer.Config;
using ACT.SpecialSpellTimer.Utility;
using Advanced_Combat_Tracker;

namespace ACT.SpecialSpellTimer.FFXIVHelper
{
    public class FFXIV
    {
        #region Singleton

        private static FFXIV instance = new FFXIV();

        private FFXIV()
        {
        }

        public static FFXIV Instance => instance;

        #endregion Singleton

        /// <summary>
        /// FFXIV_ACT_Plugin
        /// </summary>
        private volatile dynamic plugin;

        /// <summary>
        /// FFXIV_ACT_Plugin.Parse.CombatantHistory
        /// </summary>
        private volatile dynamic pluginCombatantHistory;

        /// <summary>
        /// FFXIV_ACT_Plugin.MemoryScanSettings
        /// </summary>
        private volatile dynamic pluginConfig;

        /// <summary>
        /// FFXIV_ACT_Plugin.Parse.LogParse
        /// </summary>
        private volatile dynamic pluginLogParse;

        /// <summary>
        /// FFXIV_ACT_Plugin.Memory.Memory
        /// </summary>
        private volatile dynamic pluginMemory;

        /// <summary>
        /// FFXIV_ACT_Plugin.Memory.ScanCombatants
        /// </summary>
        private volatile dynamic pluginScancombat;

        /// <summary>
        /// ACTプラグイン型のプラグインオブジェクトのインスタンス
        /// </summary>
        private IActPluginV1 ActPlugin => (IActPluginV1)this.plugin;

        public bool IsAvalable
        {
            get
            {
                if (ActGlobals.oFormActMain == null ||
                    ActGlobals.oFormActMain.IsDisposed ||
                    !ActGlobals.oFormActMain.IsHandleCreated ||
                    !ActGlobals.oFormActMain.Visible ||
                    this.plugin == null ||
                    this.pluginConfig == null ||
                    this.pluginScancombat == null ||
                    this.pluginCombatantHistory == null ||
                    this.Process == null)
                {
                    return false;
                }

                return true;
            }
        }

        public Process Process => (Process)this.pluginConfig?.Process;

        public IReadOnlyList<Zone> ZoneList => this.zoneList;

        /// <summary>
        /// ACTプラグインアセンブリ
        /// </summary>
        private Assembly FFXIVPluginAssembly => this.ActPlugin?.GetType()?.Assembly;

        #region Combatants

        private readonly IReadOnlyList<Combatant> EmptyCombatantList = new List<Combatant>();

        private volatile IReadOnlyDictionary<uint, Combatant> combatantDictionary;
        private volatile IReadOnlyList<Combatant> combatantList;
        private object combatantListLock = new object();

        private volatile List<uint> currentPartyIDList = new List<uint>();
        private object currentPartyIDListLock = new object();

#if false
        // とりあえずはリストを直接外部に公開しないことにする
        public IReadOnlyDictionary<uint, Combatant> CombatantDictionary => this.combatantDictionary;
        public IReadOnlyList<Combatant> CombatantList => this.combatantList;
        public object CombatantListLock => this.combatantListLock;

        public IReadOnlyCollection<uint> CurrentPartyIDList => this.currentPartyIDList;
        public object CurrentPartyIDListLock => this.currentPartyIDListLock;
#endif

        #endregion Combatants

        #region Resources

        private volatile IReadOnlyDictionary<int, Buff> buffList = new Dictionary<int, Buff>();
        private volatile IReadOnlyDictionary<int, Skill> skillList = new Dictionary<int, Skill>();
        private volatile IReadOnlyList<Zone> zoneList = new List<Zone>();

        #endregion Resources

        #region Start/End

        private BackgroundWorker attachFFXIVPluginWorker;
        private double scanFFXIVDurationAvg;
        private BackgroundWorker scanFFXIVWorker;

        public void End()
        {
            this.attachFFXIVPluginWorker?.Cancel();
            this.scanFFXIVWorker?.Cancel();
        }

        public void Start()
        {
            this.attachFFXIVPluginWorker = new BackgroundWorker();
            this.attachFFXIVPluginWorker.WorkerSupportsCancellation = true;
            this.attachFFXIVPluginWorker.DoWork += (s, e) =>
            {
                Thread.Sleep(1000);

                while (true)
                {
                    try
                    {
                        if (this.attachFFXIVPluginWorker.CancellationPending)
                        {
                            e.Cancel = true;
                            return;
                        }

                        this.Attach();
                        this.LoadZoneList();
                        /*
                        // 使わないのでロードしない
                        this.LoadSkillList();
                        this.LoadBuffList();
                        */
                    }
                    catch (Exception ex)
                    {
                        Logger.Write("attach ffxiv plugin error:", ex);
                        Thread.Sleep(5000);
                    }

                    Thread.Sleep(5000);
                }
            };

            this.attachFFXIVPluginWorker.RunWorkerAsync();

            this.scanFFXIVWorker = new BackgroundWorker();
            this.scanFFXIVWorker.WorkerSupportsCancellation = true;
            this.scanFFXIVWorker.DoWork += (s, e) =>
            {
                Thread.Sleep(1500);

                while (true)
                {
                    var interval = (int)Settings.Default.LogPollSleepInterval;

                    try
                    {
                        if (this.scanFFXIVWorker.CancellationPending)
                        {
                            e.Cancel = true;
                            return;
                        }

                        if (!this.IsAvalable)
                        {
                            Thread.Sleep(5000);
                            continue;
                        }

                        var sw = Stopwatch.StartNew();

                        // CombatantとパーティIDリストを更新する
                        this.RefreshCombatantList();
                        this.RefreshCurrentPartyIDList();

                        sw.Stop();
                        var duration = sw.ElapsedMilliseconds;

                        // 処理時間の平均値を算出する
                        this.scanFFXIVDurationAvg =
                            (this.scanFFXIVDurationAvg + duration) /
                            (this.scanFFXIVDurationAvg != 0 ? 2 : 1);

#if DEBUG
                        Debug.WriteLine($"Scan FFXIV duration {this.scanFFXIVDurationAvg:N1} ms");
#endif

                        // 待機時間の補正率を算出する
                        var correctionRate = 1.0d;
                        if (this.scanFFXIVDurationAvg != 0 &&
                            duration != 0)
                        {
                            correctionRate = duration / this.scanFFXIVDurationAvg;
                        }

                        // 待機時間を補正する
                        interval = (int)(interval * correctionRate);

                        // ただし極端に短くしない
                        if (interval < 10)
                        {
                            interval = 10;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Write("scan ffxiv error:", ex);
                        Thread.Sleep(5000);
                    }

                    Thread.Sleep(interval);
                }
            };

            this.scanFFXIVWorker.RunWorkerAsync();

            // XIVDBをロードする
            XIVDB.Instance.Load();
        }

        #endregion Start/End

        #region Methods

        public IReadOnlyDictionary<uint, Combatant> GetCombatantDictionaly()
        {
            if (this.combatantDictionary == null)
            {
                return null;
            }

            lock (this.combatantListLock)
            {
                return new Dictionary<uint, Combatant>(
                    (Dictionary<uint, Combatant>)this.combatantDictionary);
            }
        }

        public IReadOnlyList<Combatant> GetCombatantList()
        {
            if (this.combatantList == null)
            {
                return this.EmptyCombatantList;
            }

            lock (this.combatantListLock)
            {
                return new List<Combatant>(this.combatantList);
            }
        }

        public int GetCurrentZoneID()
        {
            return this.pluginCombatantHistory?.CurrentZoneID ?? 0;
        }

        public IReadOnlyList<Combatant> GetPartyList()
        {
            var combatants = this.GetCombatantDictionaly();

            if (combatants == null ||
                combatants.Count < 1)
            {
                return this.EmptyCombatantList;
            }

            var partyIDs = default(List<uint>);

            lock (this.currentPartyIDListLock)
            {
                partyIDs = new List<uint>(this.currentPartyIDList);
            }

            var partyList = (
                from id in partyIDs
                where
                combatants.ContainsKey(id)
                select
                combatants[id]).ToList();

            return partyList;
        }

        /// <summary>
        /// パーティをロールで分類して取得する
        /// </summary>
        /// <returns>
        /// ロールで分類したパーティリスト</returns>
        public IReadOnlyList<CombatantsByRole> GetPatryListByRole()
        {
            var list = new List<CombatantsByRole>();

            var partyList = this.GetPartyList();

            var tanks = partyList
                .Where(x => x.AsJob().Role == JobRoles.Tank)
                .ToList();

            var dpses = partyList
                .Where(x =>
                    x.AsJob().Role == JobRoles.MeleeDPS ||
                    x.AsJob().Role == JobRoles.RangeDPS ||
                    x.AsJob().Role == JobRoles.MagicDPS)
                .ToList();

            var melees = partyList
                .Where(x => x.AsJob().Role == JobRoles.MeleeDPS)
                .ToList();

            var ranges = partyList
                .Where(x => x.AsJob().Role == JobRoles.RangeDPS)
                .ToList();

            var magics = partyList
                .Where(x => x.AsJob().Role == JobRoles.MagicDPS)
                .ToList();

            var healers = partyList
                .Where(x => x.AsJob().Role == JobRoles.Healer)
                .ToList();

            if (tanks.Any())
            {
                list.Add(new CombatantsByRole(
                    JobRoles.Tank,
                    "TANK",
                    tanks));
            }

            if (dpses.Any())
            {
                list.Add(new CombatantsByRole(
                    JobRoles.DPS,
                    "DPS",
                    dpses));
            }

            if (melees.Any())
            {
                list.Add(new CombatantsByRole(
                    JobRoles.MeleeDPS,
                    "MELEE",
                    melees));
            }

            if (ranges.Any())
            {
                list.Add(new CombatantsByRole(
                    JobRoles.RangeDPS,
                    "RANGE",
                    ranges));
            }

            if (magics.Any())
            {
                list.Add(new CombatantsByRole(
                    JobRoles.MagicDPS,
                    "MAGIC",
                    magics));
            }

            if (healers.Any())
            {
                list.Add(new CombatantsByRole(
                    JobRoles.Healer,
                    "HEALER",
                    healers));
            }

            return list;
        }

        public Combatant GetPlayer()
        {
            if (this.combatantList == null)
            {
                return null;
            }

            lock (this.combatantListLock)
            {
                return this.combatantList.FirstOrDefault();
            }
        }

        public void RefreshCombatantList()
        {
            if (!this.IsAvalable)
            {
                return;
            }

            var newList = new List<Combatant>();
            var newDictionary = new Dictionary<uint, Combatant>();

            dynamic list = this.pluginScancombat.GetCombatantList();
            foreach (dynamic item in list.ToArray())
            {
                if (item == null)
                {
                    continue;
                }

                var combatant = new Combatant();

                combatant.ID = (uint)item.ID;
                combatant.OwnerID = (uint)item.OwnerID;
                combatant.Job = (int)item.Job;
                combatant.type = (byte)item.type;
                combatant.Level = (int)item.Level;
                combatant.CurrentHP = (int)item.CurrentHP;
                combatant.MaxHP = (int)item.MaxHP;
                combatant.CurrentMP = (int)item.CurrentMP;
                combatant.MaxMP = (int)item.MaxMP;
                combatant.CurrentTP = (int)item.CurrentTP;

                // 名前を登録する
                // TYPEによって分岐するため先にTYPEを設定しておくこと
                combatant.SetName((string)item.Name);

                newList.Add(combatant);
                newDictionary.Add(combatant.ID, combatant);
            }

            lock (this.combatantListLock)
            {
                this.combatantList = newList;
                this.combatantDictionary = newDictionary;
            }
        }

        public void RefreshCurrentPartyIDList()
        {
            if (!this.IsAvalable)
            {
                return;
            }

            var partyList = pluginScancombat.GetCurrentPartyList(
                out int partyCount) as List<uint>;

            lock (this.currentPartyIDListLock)
            {
                this.currentPartyIDList = partyList;
            }
        }

        /// <summary>
        /// 文中に含まれるパーティメンバの名前を設定した形式に置換する
        /// </summary>
        /// <param name="text">置換対象のテキスト</param>
        /// <returns>
        /// 置換後のテキスト</returns>
        public string ReplacePartyMemberName(
            string text)
        {
            var r = text;

            var party = this.GetPartyList();

            foreach (var pc in party)
            {
                switch (Settings.Default.PCNameInitialOnDisplayStyle)
                {
                    case NameStyles.FullName:
                        r = r.Replace(pc.NameFI, pc.Name);
                        r = r.Replace(pc.NameIF, pc.Name);
                        r = r.Replace(pc.NameII, pc.Name);
                        break;

                    case NameStyles.FullInitial:
                        r = r.Replace(pc.Name, pc.NameFI);
                        break;

                    case NameStyles.InitialFull:
                        r = r.Replace(pc.Name, pc.NameIF);
                        break;

                    case NameStyles.InitialInitial:
                        r = r.Replace(pc.Name, pc.NameII);
                        break;
                }
            }

            return r;
        }

        private void Attach()
        {
            this.AttachPlugin();
            this.AttachScanMemory();
        }

        private void AttachPlugin()
        {
            if (this.plugin != null ||
                ActGlobals.oFormActMain == null ||
                ActGlobals.oFormActMain.IsDisposed ||
                !ActGlobals.oFormActMain.IsHandleCreated ||
                !ActGlobals.oFormActMain.Visible)
            {
                return;
            }

            var ffxivPlugin = (
                from x in ActGlobals.oFormActMain.ActPlugins
                where
                x.pluginFile.Name.ToUpper().Contains("FFXIV_ACT_Plugin".ToUpper()) &&
                x.lblPluginStatus.Text.ToUpper().Contains("FFXIV Plugin Started.".ToUpper())
                select
                x.pluginObj).FirstOrDefault();

            if (ffxivPlugin != null)
            {
                this.plugin = ffxivPlugin;
                Logger.Write("attached ffxiv plugin.");
            }
        }

        private void AttachScanMemory()
        {
            if (this.plugin == null)
            {
                return;
            }

            FieldInfo fi;

            if (this.pluginLogParse == null)
            {
                fi = this.plugin?.GetType().GetField(
                    "_LogParse",
                    BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
                this.pluginLogParse = fi?.GetValue(this.plugin);
            }

            if (this.pluginCombatantHistory == null)
            {
                var settings = this.pluginLogParse?.Settings;
                if (settings != null)
                {
                    fi = settings?.GetType().GetField(
                        "CombatantHistory",
                    BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
                    this.pluginCombatantHistory = fi?.GetValue(settings);
                }
            }

            if (this.pluginMemory == null)
            {
                fi = this.plugin?.GetType().GetField(
                    "_Memory",
                    BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
                this.pluginMemory = fi?.GetValue(this.plugin);
            }

            if (this.pluginMemory == null)
            {
                return;
            }

            if (this.pluginConfig == null)
            {
                fi = this?.pluginMemory?.GetType().GetField(
                    "_config",
                    BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
                this.pluginConfig = fi?.GetValue(this.pluginMemory);
            }

            if (this.pluginConfig == null)
            {
                return;
            }

            if (this.pluginScancombat == null)
            {
                fi = this.pluginConfig?.GetType().GetField(
                    "ScanCombatants",
                    BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
                this.pluginScancombat = fi?.GetValue(this.pluginConfig);

                Logger.Write("attached ffxiv plugin ScanCombatants.");
            }
        }

        private void LoadBuffList()
        {
            if (this.buffList.Any())
            {
                return;
            }

            if (this.plugin == null)
            {
                return;
            }

            var asm = this.plugin.GetType().Assembly;

            var language = Settings.Default.Language;
            var resourcesName = $"FFXIV_ACT_Plugin.Resources.BuffList_{language.ToUpper()}.txt";

            using (var st = asm.GetManifestResourceStream(resourcesName))
            {
                if (st == null)
                {
                    return;
                }

                var newList = new Dictionary<int, Buff>();

                using (var sr = new StreamReader(st))
                {
                    while (!sr.EndOfStream)
                    {
                        var line = sr.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var values = line.Split('|');
                            if (values.Length >= 2)
                            {
                                var buff = new Buff()
                                {
                                    ID = int.Parse(values[0], NumberStyles.HexNumber),
                                    Name = values[1].Trim()
                                };

                                newList.Add(buff.ID, buff);
                            }
                        }
                    }
                }

                this.buffList = newList;
                Logger.Write("buff list loaded.");
            }
        }

        private void LoadSkillList()
        {
            if (this.skillList.Any())
            {
                return;
            }

            if (this.plugin == null)
            {
                return;
            }

            var asm = this.plugin.GetType().Assembly;

            var language = Settings.Default.Language;
            var resourcesName = $"FFXIV_ACT_Plugin.Resources.SkillList_{language.ToUpper()}.txt";

            using (var st = asm.GetManifestResourceStream(resourcesName))
            {
                if (st == null)
                {
                    return;
                }

                var newList = new Dictionary<int, Skill>();

                using (var sr = new StreamReader(st))
                {
                    while (!sr.EndOfStream)
                    {
                        var line = sr.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var values = line.Split('|');
                            if (values.Length >= 2)
                            {
                                var skill = new Skill()
                                {
                                    ID = int.Parse(values[0], NumberStyles.HexNumber),
                                    Name = values[1].Trim()
                                };

                                newList.Add(skill.ID, skill);
                            }
                        }
                    }
                }

                this.skillList = newList;
                Logger.Write("skill list loaded.");
            }
        }

        private void LoadZoneList()
        {
            if (this.zoneList.Any())
            {
                return;
            }

            if (this.plugin == null)
            {
                return;
            }

            var newList = new List<Zone>();

            var asm = this.plugin.GetType().Assembly;

            var language = "EN";
            var resourcesName = $"FFXIV_ACT_Plugin.Resources.ZoneList_{language.ToUpper()}.txt";

            using (var st = asm.GetManifestResourceStream(resourcesName))
            {
                if (st == null)
                {
                    return;
                }

                using (var sr = new StreamReader(st))
                {
                    while (!sr.EndOfStream)
                    {
                        var line = sr.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var values = line.Split('|');
                            if (values.Length >= 2)
                            {
                                var zone = new Zone()
                                {
                                    ID = int.Parse(values[0]),
                                    Name = values[1].Trim()
                                };

                                newList.Add(zone);
                            }
                        }
                    }
                }

                // ユーザで任意に追加したゾーンを読み込む
                this.LoadZoneListAdded(newList);

                // 新しいゾーンリストをセットする
                this.zoneList = newList;

                // ゾーンリストを翻訳する
                this.TranslateZoneList();

                Logger.Write("zone list loaded.");
            }
        }

        private void LoadZoneListAdded(List<Zone> baseZoneList)
        {
            try
            {
                var dir = XIVDB.Instance.ResourcesDirectory;
                var file = Path.Combine(dir, "Zones.csv");

                if (!File.Exists(file))
                {
                    return;
                }

                using (var sr = new StreamReader(file, new UTF8Encoding(false)))
                {
                    while (!sr.EndOfStream)
                    {
                        var line = sr.ReadLine();
                        var values = line.Split(',');

                        if (values.Length >= 2)
                        {
                            var newZone = new Zone()
                            {
                                ID = int.Parse(values[0]),
                                Name = values[1].Trim(),
                                IsAddedByUser = true,
                            };

                            var oldZone = baseZoneList.FirstOrDefault(x =>
                                x.ID == newZone.ID);

                            if (oldZone != null)
                            {
                                oldZone.Name = newZone.Name;
                                oldZone.IsAddedByUser = true;
                            }
                            else
                            {
                                baseZoneList.Add(newZone);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("error on Load Extra Zonelist.", ex);
            }
        }

        private void TranslateZoneList()
        {
            if (Settings.Default.Language != "JP")
            {
                return;
            }

            foreach (var zone in this.ZoneList)
            {
                var place = (
                    from x in XIVDB.Instance.PlacenameList.AsParallel()
                    where
                    string.Equals(x.NameEn, zone.Name, StringComparison.InvariantCultureIgnoreCase)
                    select
                    x).FirstOrDefault();

                if (place != null)
                {
                    zone.Name = place.Name;
                    zone.IDonDB = place.ID;
                }
                else
                {
                    var area = (
                        from x in XIVDB.Instance.AreaList.AsParallel()
                        where
                        string.Equals(x.NameEn, zone.Name, StringComparison.InvariantCultureIgnoreCase)
                        select
                        x).FirstOrDefault();

                    if (area != null)
                    {
                        zone.Name = area.Name;
                        zone.IDonDB = area.ID;
                    }
                }
            }
        }

        #endregion Methods

        #region Sub classes

        public class CombatantsByRole
        {
            public CombatantsByRole(
                JobRoles roleType,
                string roleLabel,
                IReadOnlyList<Combatant> combatants)
            {
                this.RoleType = roleType;
                this.RoleLabel = roleLabel;
                this.Combatants = combatants;
            }

            public IReadOnlyList<Combatant> Combatants { get; set; }
            public string RoleLabel { get; set; }
            public JobRoles RoleType { get; set; }
        }

        #endregion Sub classes
    }
}
