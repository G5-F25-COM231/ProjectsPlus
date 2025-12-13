namespace t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces
{
    public interface IChannelAdapterFactory
    {
        IChannelAdapter GetAdapter(ChannelType channel);
    }

}
