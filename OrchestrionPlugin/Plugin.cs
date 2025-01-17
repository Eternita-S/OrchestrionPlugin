﻿using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Linq;

namespace OrchestrionPlugin
{
    // TODO:
    // try to find what writes to bgm 0, block it if we are playing?
    //   or save/restore if we preempt it?
    // debug info of which priority is active
    //  notifications/logs of changes even to lower priorities?

    public class Plugin : IDalamudPlugin, IPlaybackController, IResourceLoader
    {
        public string Name => "Orchestrion plugin";

        private const string songListFile = "xiv_bgm.csv";
        private const string commandName = "/porch";

        internal DalamudPluginInterface pi;
        internal Configuration configuration;
        internal SongList songList;
        internal BGMControl bgmControl;
        internal SongReplacementList songReplacementCfg;
        private string localDir;
        //private SeString nowPlayingString;
        //private TextPayload currentSongPayload;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pi = pluginInterface;

            this.configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.configuration.Initialize(pluginInterface);
            this.enableFallbackPlayer = this.configuration.UseOldPlayback;
            
            this.localDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var songlistPath = Path.Combine(this.localDir, songListFile);
            this.songList = new SongList(songlistPath, this.configuration, this, this);

            // TODO: eventually it might be nice to do this only if the fallback player isn't being used
            // and to add/remove it on-demand if that changes
            var addressResolver = new AddressResolver();
            try
            {
                addressResolver.Setup(pluginInterface.TargetModuleScanner);
                this.bgmControl = new BGMControl(addressResolver);
                this.bgmControl.OnSongChanged += HandleSongChanged;
                this.bgmControl.OnSongChanged2 += HandleSongChanged2;
                this.bgmControl.StartUpdate();
            }
            catch (Exception e)
            {
                PluginLog.LogError(e, "Failed to find BGM playback objects");
                this.bgmControl?.Dispose();
                this.bgmControl = null;

                this.enableFallbackPlayer = true;
            }

            // TODO: for new payload system
            // cached string so we don't have to rebuild this entire payload set each time
            //this.currentSongPayload = new TextPayload("");           // dummy, filled in when needed
            //this.nowPlayingString = new SeString(new Payload[] {
            //    new TextPayload("Now playing "),
            //    EmphasisItalicPayload.ItalicsOn,
            //    this.currentSongPayload,                  
            //    EmphasisItalicPayload.ItalicsOff,
            //    new TextPayload(".")
            //});

            // caches all the payloads - future updates will only have to reencode the song name payload
            // this.nowPlayingString.Encode();

            pluginInterface.CommandManager.AddHandler(commandName, new CommandInfo(OnDisplayCommand)
            {
                HelpMessage = "Displays the orchestrion player, to view, change, or stop in-game BGM."
            });
            pluginInterface.UiBuilder.OnBuildUi += Display;

            pluginInterface.UiBuilder.OnOpenConfigUi += (sender, args) => this.songList.SettingsVisible = true;
            this.songReplacementCfg = new SongReplacementList(this);
        }

        public void Dispose()
        {
            this.songList.Dispose();
            this.bgmControl?.Dispose();

            this.pi.UiBuilder.OnBuildUi -= Display;
            this.pi.CommandManager.RemoveHandler(commandName);

            this.songReplacementCfg.Dispose();

            this.pi.Dispose();
        }

        private void OnDisplayCommand(string command, string args)
        {
            if (!string.IsNullOrEmpty(args) && args.Split(' ')[0].ToLowerInvariant() == "debug")
            {
                this.songList.AllowDebug = !this.songList.AllowDebug;
                this.pi.Framework.Gui.Chat.Print($"Orchestrion debug options have been {(this.songList.AllowDebug ? "enabled" : "disabled")}.");
            }
            else if (!string.IsNullOrEmpty(args) && args.Split(' ')[0].ToLowerInvariant() == "r")
            {
                this.songReplacementCfg.open = true;                
            }
            else
            {
                // might be better to fully add/remove the OnBuildUi handler
                this.songList.Visible = true;
            }
        }

        private void Display()
        {
            this.songList.Draw();
        }

        private void HandleSongChanged(ushort songId)
        {
            if (this.configuration.ShowSongInChat && !EnableFallbackPlayer) // hack to not show 'new' updates when using the old player... temporary hopefully
            {
                var songName = this.songList.GetSongTitle(songId);
                if (!string.IsNullOrEmpty(songName))
                {
                    var messageBytes = new List<byte>();

                    messageBytes.AddRange(Encoding.UTF8.GetBytes("Now playing "));
                    messageBytes.AddRange(new byte[] { 0x02, 0x1A, 0x02, 0x02, 0x03 });
                    messageBytes.AddRange(Encoding.UTF8.GetBytes(songName));
                    messageBytes.AddRange(new byte[] { 0x02, 0x1A, 0x02, 0x01, 0x03 });
                    messageBytes.Add((byte)'.');

                    this.pi.Framework.Gui.Chat.PrintChat(new XivChatEntry
                    {
                        MessageBytes = messageBytes.ToArray(),
                        Type = XivChatType.Echo
                    });

                    //this.currentSongPayload.Text = songName;

                    //this.pi.Framework.Gui.Chat.PrintChat(new XivChatEntry
                    //{
                    //    MessageBytes = this.nowPlayingString.Encode(),
                    //    Type = XivChatType.Echo
                    //});
                }
            }
        }

        internal void HandleSongChanged2(ushort songId)
        {
            if (!configuration.EnableReplacer) return;
            //if (configuration.DisplayAreaBGMChanged) pi.Framework.Gui.Chat.Print("Area song: "+songList.GetSongTitle(songId));
            if (!this.IsUserSelected)
            {
                if (configuration.SongReplacements.ContainsKey(songId))
                {
                    var randomId = configuration.SongReplacements[songId].ElementAt(new Random().Next(0, configuration.SongReplacements[songId].Count));
                    PlaySong(randomId);
                    if (configuration.DisplayAreaBGMChanged)
                    {
                        pi.Framework.Gui.Toast.ShowQuest("Song replaced from [" + songList.GetSongTitle(songId) + "] to [" + songList.GetSongTitle((ushort)randomId) + "]");
                    }
                }
                else if(bgmControl.CurrentSongId != bgmControl.CurrentSongId2)
                {
                    StopSong();
                }
            }
        }

        #region IPlaybackController

        private bool enableFallbackPlayer;
        public bool EnableFallbackPlayer
        {
            get { return enableFallbackPlayer; }
            set
            {
                // we should probably kill bgmControl's update loop when we disable it
                // but this is hopefully completely temporary anyway

                // if we force disabled due to a failed load, don't allow changing
                if (this.bgmControl != null)
                {
                    enableFallbackPlayer = value;
                    this.configuration.UseOldPlayback = value;
                    this.configuration.Save();
                }
            }
        }

        public int CurrentSong => EnableFallbackPlayer ? 0 : this.bgmControl.CurrentSongId;

        public bool IsUserSelected { get; set; } = false;

        public void PlaySong(int songId)
        {
            if (EnableFallbackPlayer)
            {
                this.pi.CommandManager.Commands["/xlbgmset"].Handler("/xlbgmset", songId.ToString());
            }
            else
            {
                this.bgmControl.SetSong((ushort)songId, this.configuration.TargetPriority);
            }
        }

        public void StopSong()
        {
            if (EnableFallbackPlayer)
            {
                // still no real way to do this
                this.pi.CommandManager.Commands["/xlbgmset"].Handler("/xlbgmset", "9999");
            }
            else
            {
                this.bgmControl.SetSong(0, this.configuration.TargetPriority);
            }
        }

        public void DumpDebugInformation()
        {
            this.bgmControl?.DumpPriorityInfo();
        }

        #endregion

        #region IResourceLoader

        public ImGuiScene.TextureWrap LoadUIImage(string imageFile)
        {
            var path = Path.Combine(this.localDir, imageFile);
            return this.pi.UiBuilder.LoadImage(path);
        }

        #endregion
    }
}
