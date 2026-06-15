namespace BiddingBuddy.Bff.Core.Options;

public class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    public string HostName    { get; set; } = "localhost";
    public int    Port        { get; set; } = 5672;
    public string Username    { get; set; } = "guest";
    public string Password    { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Dead-letter exchange shared with the BidProcessor pipeline.</summary>
    public string DeadLetterExchange { get; set; } = "bid.dlx";

    /// <summary>Connection name shown in the RabbitMQ management UI.</summary>
    public string ClientName { get; set; } = "BiddingBuddyBFF";
}
