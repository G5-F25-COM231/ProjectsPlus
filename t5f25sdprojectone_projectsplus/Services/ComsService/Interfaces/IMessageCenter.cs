// src/ProjectsPlus.Comms/Realtime/IMessageCenter.cs
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces
{
    public interface IMessageCenter
    {
        Task HandleRealtimeInboundAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct = default);
    }
}

// src/ProjectsPlus.Comms/Realtime/IChatroomService.cs


//namespace t5f25sdprojectone_projectsplus.Services.ComsService
//{
//    public interface IChatroomService
//    {
//        Task AddConnectionToRoomAsync(string roomId, string connectionId, Guid? userId);
//        Task RemoveConnectionFromRoomAsync(string roomId, string connectionId, Guid? userId);
//        Task HandleRealtimeMessageAsync(RealtimeEnvelope envelope, string connectionId, Guid? userId, CancellationToken ct = default);
//    }
//}
