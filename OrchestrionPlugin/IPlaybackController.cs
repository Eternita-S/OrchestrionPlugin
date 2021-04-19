
namespace OrchestrionPlugin
{
    interface IPlaybackController
    {
        int CurrentSong { get; }
        bool EnableFallbackPlayer { get; set; }
        bool IsUserSelected { get; set; }
        void PlaySong(int songId);
        void StopSong();

        void DumpDebugInformation();
    }
}
