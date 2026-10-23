using System.Text.Json;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Lobby;
using Smart_Training_Server.Services.Room;

internal static class LobbyParticipantsChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name)
        {
            if (!ok)
            {
                throw new Exception(name);
            }

            Console.WriteLine("PASS: " + name);
        }

        var clock = new TestClock();
        var users = new UserService(Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20
                }));
        var lobbies = TestCatalogLobbies.Create(users, "participants", "foreign");
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20,
                    MinUsers = 1,
                    MaxUsers = 8
                }),
            clock
        );
        var a = users.Login(new LoginRequest { UserName = "Alpha" });
        var b = users.Login(new LoginRequest { UserName = "beta" });
        var outsider = users.Login(new LoginRequest { UserName = "outside" });
        var lobby = TestCatalogLobbies.Get(lobbies, "participants");
        lobby.Contents["test"] = new ContentInfo
        {
            RootContent = "test"
        };
        var foreign = TestCatalogLobbies.Get(lobbies, "foreign");
        Check(
            rooms.GetLobbyParticipants("missing", a.User!.Id).StatusCode == 404,
            "participants missing lobby returns 404"
        );
        Check(
            rooms.GetLobbyParticipants(lobby.Id, a.User!.Id).StatusCode == 403,
            "participants require actual lobby entry"
        );
        foreach (var login in new[]
        {
            a,
            b
        }

        )
        {
            await lobbies.JoinLobby(login.User!.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
        }

        await lobbies.JoinLobby(outsider.User!.Id, new LobbyMembershipRequest { LobbyId = foreign.Id });
        Check(
            rooms.GetLobbyParticipants(lobby.Id, outsider.User!.Id).StatusCode == 403 && rooms.GetLobbyParticipants(lobby.Id, "fake").StatusCode == 403,
            "participants reject foreign lobby and unknown caller"
        );
        var initial = rooms.GetLobbyParticipants(lobby.Id, a.User!.Id);
        Check(
            initial.StatusCode == 200 && initial.Participants.Count == 2 && initial.Participants.All(item => item.ConnectionState == "lobby" && item.RoomId == ""),
            "participants start as lobby waiting"
        );
        Check(
            initial.Participants.Select(item => item.Nickname).SequenceEqual(new[] { "Alpha", "beta" }),
            "participants have stable nickname ordering"
        );
        var room = TestRoomSetup.Create(rooms,
            a.User.Id,
            new CreateRoomRequest {
                LobbyId = lobby.Id,
                RoomName = "room",
                ContentId = "test",
                MaxUsers = 4
            }
        ).Room!;
        room.State = Smart_Training_Server.Enum.ServerState.Starting;
        room.ServerToken = "secret-server-token";
        rooms.RegisterServer(room.Id, room.ServerToken, "participants-server");
        var admission = await rooms.JoinRoom(a.User.Id, new JoinRoomRequest {
                LobbyId = lobby.Id,
                RoomId = room.Id
            });
        var reserved = rooms.GetLobbyParticipants(lobby.Id, b.User!.Id).Participants.Single(item => item.UserId == a.User.Id);
        Check(
            reserved.ConnectionState == "reserved" && reserved.RoomName == "room" && !reserved.IsRoomMaster,
            "participants distinguish reservation from connected master"
        );
        rooms.ConfirmConnection(room.Id, "participants-server", a.User.Id, admission.AdmissionToken);
        var connected = rooms.GetLobbyParticipants(lobby.Id, b.User.Id).Participants.Single(item => item.UserId == a.User.Id);
        Check(
            connected.ConnectionState == "connected" && connected.IsRoomMaster && connected.RoomId == room.Id && !connected.RoomIsRunning,
            "participants show confirmed master with current room metadata"
        );
        Check(
            initial.Participants.All(item => item.ConnectionState == "lobby"),
            "participants responses are immutable snapshots rather than live user references"
        );
        var json = JsonSerializer.Serialize(rooms.GetLobbyParticipants(lobby.Id, a.User.Id).Participants);
        Check(
            !json.Contains(admission.AdmissionToken) && !json.Contains(a.AccessToken!) && !json.Contains("ServerToken") && !json.Contains("IpAddress"),
            "participants payload excludes tokens and server addresses"
        );
        var updates = new LobbyRoomUpdates(users, rooms, lobbies);
        updates.Subscribe("watch", lobby.Id, "Bearer " + a.AccessToken);
        updates.CollectChanges();
        Check(updates.CollectChanges().Count == 0, "participant snapshots suppress unchanged notifications");
        // Replace a lobby-only user between ticks: counts and all room summaries remain unchanged.
        lobbies.LeaveLobby(b.User.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
        var replacement = users.Login(new LoginRequest { UserName = "replacement" });
        await lobbies.JoinLobby(replacement.User!.Id, new LobbyMembershipRequest { LobbyId = lobby.Id });
        Check(
            lobby.Users.Count == 2 && updates.CollectChanges().Single().ConnectionId == "watch",
            "live participants detect same-count membership replacement"
        );
        var replaced = rooms.GetLobbyParticipants(lobby.Id, a.User.Id).Participants;
        Check(
            replaced.All(item => item.UserId != b.User.Id) && replaced.Any(item => item.UserId == replacement.User!.Id),
            "departed nickname disappears and new member appears"
        );
        room.Name = "renamed";
        room.IsRunning = true;
        var renamed = rooms.GetLobbyParticipants(lobby.Id, a.User.Id).Participants.Single(item => item.UserId == a.User.Id);
        Check(
            renamed.RoomName == "renamed" && renamed.RoomIsRunning,
            "participants read authoritative room rename and phase"
        );
        room.IsRunning = false;
        var pending = await rooms.JoinRoom(replacement.User!.Id, new JoinRoomRequest {
                LobbyId = lobby.Id,
                RoomId = room.Id
            });
        clock.Advance(TimeSpan.FromMinutes(2));
        Check(
            rooms.GetLobbyParticipants(lobby.Id, a.User.Id).Participants.Single(item => item.UserId == replacement.User!.Id).ConnectionState == "lobby" && replacement.User.PendingRoomId == "",
            "participant query expires abandoned reservation before returning state"
        );
        var fresh = await rooms.JoinRoom(replacement.User.Id, new JoinRoomRequest {
                LobbyId = lobby.Id,
                RoomId = room.Id
            });
        rooms.ConfirmConnection(room.Id, "participants-server", replacement.User.Id, fresh.AdmissionToken);
        rooms.TransferRoomMaster(
            a.User.Id,
            new RoomParticipantRequest {
                LobbyId = lobby.Id,
                RoomId = room.Id,
                TargetUserId = replacement.User.Id
            }
        );
        var transferred = rooms.GetLobbyParticipants(lobby.Id, a.User.Id).Participants;
        Check(
            transferred.Single(item => item.UserId == replacement.User.Id).IsRoomMaster && !transferred.Single(item => item.UserId == a.User.Id).IsRoomMaster,
            "participant roles follow manual master handover"
        );
        rooms.ClientDisconnected(room.Id, "participants-server", replacement.User.Id, fresh.AdmissionToken);
        var disconnected = rooms.GetLobbyParticipants(lobby.Id, a.User.Id).Participants;
        Check(
            disconnected.Single(item => item.UserId == replacement.User.Id).ConnectionState == "lobby" && disconnected.Single(item => item.UserId == a.User.Id).IsRoomMaster,
            "disconnect returns participant to lobby and updates automatic master role"
        );
        rooms.ServerDisconnected(room.Id, "participants-server");
        Check(
            rooms.GetLobbyParticipants(lobby.Id, a.User.Id).Participants.All(item => item.ConnectionState == "lobby" && item.RoomId == ""),
            "server reset clears participant room states"
        );
        a.User.CurrentLobbyId = foreign.Id;
        Check(
            rooms.GetLobbyParticipants(lobby.Id, a.User.Id).StatusCode == 403,
            "stale lobby dictionary does not grant departed caller access"
        );
        Check(
            rooms.GetLobbyParticipants(lobby.Id, replacement.User.Id).Participants.All(item => item.UserId != a.User.Id),
            "participant snapshot excludes stale lobby membership"
        );
    }
}
