namespace BMX.Infra.log4stash.Configuration
{
    public interface IServerData
    {
        string Address { get; set; }
        int Port { get; set; }
        string Path { get; set; }
    }
}