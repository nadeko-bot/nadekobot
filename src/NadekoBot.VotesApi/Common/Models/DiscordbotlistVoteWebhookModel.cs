namespace NadekoBot.VotesApi
{
    public class DiscordbotlistVoteWebhookModel
    {        
        /// <summary>
        /// The avatar hash of the user
        /// </summary>
        public string Avatar { get; set; }
        
        /// <summary>
        /// The username of the user who voted
        /// </summary>
        public string Username { get; set; }
        
        /// <summary>
        /// The ID of the user who voted
        /// </summary>
        public string Id { get; set; }
    }
}