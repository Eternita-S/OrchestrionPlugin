using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrchestrionPlugin
{
    class SongReplacementList : IDisposable
    {
        internal Plugin p;
        internal bool open = false;
        internal bool wasOpen = false;
        internal string filter = "";
        internal string filter2 = "";
        internal bool onlyModified = false;
        internal bool onlySelected = false;
        internal bool jumpToCurrent = false;
        internal SongReplacementList(Plugin p)
        {
            this.p = p;
            p.pi.UiBuilder.OnBuildUi += Draw;
        }

        public void Dispose()
        {
            p.pi.UiBuilder.OnBuildUi -= Draw;
        }

        internal void Draw()
        {
            if (!open)
            {
                if (wasOpen)
                {
                    wasOpen = false;
                    p.configuration.Save();
                    p.pi.Framework.Gui.Toast.ShowQuest("Configuration saved", new Dalamud.Game.Internal.Gui.Toast.QuestToastOptions { DisplayCheckmark = true, PlaySound = true });
                    p.HandleSongChanged2(p.bgmControl.CurrentSongId2);
                }
                return;
            }
            wasOpen = true;
            if (ImGui.Begin("Song replacer", ref open))
            {
                ImGui.Checkbox("Enable replacer", ref p.configuration.EnableReplacer);
                ImGui.SameLine();
                ImGui.Checkbox("Display notification on screen whenever background song is replaced", ref p.configuration.DisplayAreaBGMChanged);
                ImGui.Text("Filter:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(250f);
                ImGui.InputText("##replacerfilter", ref filter, 100);
                ImGui.SameLine();
                if (ImGui.Button("X##clrflt")) filter = "";
                ImGui.SameLine();
                ImGui.Checkbox("Show only modified", ref onlyModified);
                ImGui.SameLine();
                foreach(var i in p.songList.songs)
                {
                    if(i.Value.Id == p.bgmControl.CurrentSongId2)
                    {
                        if (ImGui.Button("Jump to current song"))
                        {
                            jumpToCurrent = true;
                        }
                        break;
                    }
                }
                ImGui.BeginChild("##replacermainarea");
                foreach (var i in p.songList.songs)
                {
                    var has = p.configuration.SongReplacements.ContainsKey(i.Value.Id);
                    if (onlyModified && !has) continue;
                    if (filter.Length > 0 && !i.Value.Id.ToString().Contains(filter)
                        && !i.Value.Name.ToLower().Contains(filter.ToLower())
                        && !i.Value.Locations.ToLower().Contains(filter.ToLower())) continue;
                    var colored = false;
                    if (has)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, 0xff00aa00);
                        colored = true;
                    }
                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                    if (ImGui.BeginCombo("##" + i.Key, (i.Value.Id == p.bgmControl.CurrentSongId2?"> ":"")
                        +i.Value.Id + " | " + i.Value.Name + " ("+i.Value.Locations+")"))
                    {
                        if (colored)
                        {
                            ImGui.PopStyleColor();
                            colored = false;
                        }
                        ImGui.Text("Filter: ");
                        ImGui.SameLine();
                        ImGui.InputText("##filtersongs" + i.Key, ref filter2, 100);
                        ImGui.SameLine();
                        if (ImGui.Button("X##clrflt" + i.Key)) filter2 = "";
                        ImGui.SameLine();
                        ImGui.Checkbox("Show only selected", ref onlySelected);
                        foreach (var s in p.songList.songs)
                        {
                            if (filter2.Length > 0 && !s.Value.Id.ToString().Contains(filter2)
                                && !s.Value.Name.ToLower().Contains(filter2.ToLower())
                                && !s.Value.Locations.ToLower().Contains(filter2.ToLower())) continue;
                            if ((!onlySelected || (has && p.configuration.SongReplacements[i.Value.Id].Contains(s.Value.Id)))
                                && SongButton(s.Value.Id + " | " + s.Value.Name + " ("+s.Value.Locations+")" + "##" + i.Key,
                                has && p.configuration.SongReplacements[i.Value.Id].Contains(s.Value.Id)))
                            {
                                if (!has) p.configuration.SongReplacements.Add(i.Value.Id, new HashSet<int>());
                                if (p.configuration.SongReplacements[i.Value.Id].Contains(s.Value.Id))
                                {
                                    p.configuration.SongReplacements[i.Value.Id].Remove(s.Value.Id);
                                }
                                else
                                {
                                    p.configuration.SongReplacements[i.Value.Id].Add(s.Value.Id);
                                }
                            }
                        }
                        ImGui.EndCombo();
                    }
                    if (jumpToCurrent && i.Value.Id == p.bgmControl.CurrentSongId2)
                    {
                        jumpToCurrent = false;
                        ImGui.SetScrollHereY();
                    }
                    if (has && p.configuration.SongReplacements[i.Value.Id].Count == 0)
                    {
                        p.configuration.SongReplacements.Remove(i.Value.Id);
                    }
                    if (colored)
                    {
                        ImGui.PopStyleColor();
                        colored = false;
                    }
                }
                ImGui.EndChild();
            }
            ImGui.End();
        }

        bool SongButton(string text, bool colored)
        {
            if (colored)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, 0xff00aa00);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xff00aa00);
            }
            var a = ImGui.SmallButton(text);
            if(colored) ImGui.PopStyleColor(2);
            return a;
        }
    }
}
