namespace NadekoBot.Modules;

public class MedusaeRepositoryService : IMedusaeRepositoryService, INService
{
    public async Task<List<ModuleItem>> GetModuleItemsAsync()
    {
        // Simulate retrieving data from a database or API
        await Task.Delay(100);
        return
        [
            new()
            {
                Name = "RSS Reader",
                Description = "Keep up to date with your favorite websites",
                Command = ".meinstall rss"
            },
            new()
            {
                Name = "Password Manager",
                Description = "Safely store and manage all your passwords",
                Command = ".meinstall passwordmanager"
            },
            new()
            {
                Name = "Browser Extension",
                Description = "Enhance your browsing experience with useful tools",
                Command = ".meinstall browserextension"
            },
            new()
            {
                Name = "Video Downloader",
                Description = "Download videos from popular websites",
                Command = ".meinstall videodownloader"
            },
            new()
            {
                Name = "Virtual Private Network",
                Description = "Securely browse the web and protect your privacy",
                Command = ".meinstall vpn"
            },
            new()
            {
                Name = "Ad Blocker",
                Description = "Block annoying ads and improve page load times",
                Command = ".meinstall adblocker"
            },
            new()
            {
                Name = "Cloud Storage",
                Description = "Store and share your files online",
                Command = ".meinstall cloudstorage"
            },
            new()
            {
                Name = "Social Media Manager",
                Description = "Manage all your social media accounts in one place",
                Command = ".meinstall socialmediamanager"
            },
            new()
            {
                Name = "Code Editor",
                Description = "Write and edit code online",
                Command = ".meinstall codeeditor"
            }
        ];
    }
}