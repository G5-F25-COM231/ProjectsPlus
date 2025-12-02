namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public interface IChannelAdapterFactory
    {
        IChannelAdapter GetAdapter(ChannelType channel);
    }

}
