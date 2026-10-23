using System.Text.Json;
using Microsoft.Extensions.Options;
using Smart_Training_Server.Models;
using Smart_Training_Server.Options;
using Smart_Training_Server.Requests.Auth;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Services.Auth;
using Smart_Training_Server.Services.Room;

internal static class PasswordRoomChecks
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

        var users = new UserService(Options.Create(new AuthOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20
                }));
        var lobbies = TestCatalogLobbies.Create(users, "password");
        var rooms = new RoomService(
            lobbies,
            users,
            Options.Create(new RoomOptions {
                    NameMinLength = 1,
                    NameMaxLength = 20,
                    MinUsers = 1,
                    MaxUsers = 8
                })
        );
        var lobby = TestCatalogLobbies.Get(lobbies, "password");
        lobby.Contents["test"] = new ContentInfo
        {
            RootContent = "test"
        };
        var owner = users.Login(new LoginRequest { UserName = "owner" }).User!;
        var member = users.Login(new LoginRequest { UserName = "member" }).User!;
        await lobbies.JoinLobby(owner.Id, new() { LobbyId = lobby.Id });
        await lobbies.JoinLobby(member.Id, new() { LobbyId = lobby.Id });
        CreateRoomRequest Request(string name, string? password) => new()
        {
            LobbyId = lobby.Id,
            RoomName = name,
            ContentId = "test",
            MaxUsers = 4,
            Password = password
        };
        foreach (var invalid in new[]
        {
            "abc",
            "    ",
            new string ('x', 33)
        }

        )
        {
            Check(
                !TestRoomSetup.Create(rooms, owner.Id, Request("invalid", invalid)).Success && lobby.Rooms.IsEmpty,
                "invalid password does not create room"
            );
        }

        const string password = "Case pass 42";
        var room = TestRoomSetup.Create(rooms, owner.Id, Request("protected", password)).Room!;
        var sibling = TestRoomSetup.Create(rooms, owner.Id, Request("salted", password)).Room!;
        Check(
            room.HasPassword && room.PasswordHash.Length == 32 && room.PasswordSalt.Length == 16 && !room.PasswordHash.SequenceEqual(sibling.PasswordHash),
            "same room password uses independent salted hashes"
        );
        var publicRoom = TestRoomSetup.Create(rooms, owner.Id, Request("public", null)).Room!;
        Check(
            !publicRoom.HasPassword && !TestRoomSetup.Create(rooms, owner.Id, Request("empty", "")).Room!.HasPassword,
            "omitted and empty passwords create public rooms"
        );
        var raw = JsonSerializer.Serialize(room);
        Check(
            !raw.Contains(password) && !raw.Contains("PasswordHash") && !raw.Contains("PasswordSalt"),
            "room serialization excludes plaintext, hash and salt"
        );
        Check(
            rooms.GetRoomsByLobby(lobby.Id)!.Single(r => r.Id == room.Id).HasPassword && !rooms.GetRoomByLobby(lobby.Id, publicRoom.Id)!.HasPassword,
            "room list and detail expose protection flag only"
        );
        async Task<Smart_Training_Server.Responses.Room.JoinRoomResponse> Join(UserInfo user, string? value) => await rooms.JoinRoom(user.Id, new() {
                LobbyId = lobby.Id,
                RoomId = room.Id,
                Password = value
            });
        foreach (var incorrect in new string? []
        {
            null,
            "",
            "case pass 42",
            "bad",
            new string ('x', 33)
        }

        )
        {
            Check(
                !(await Join(owner, incorrect)).Success && owner.PendingRoomId == "" && room.ReservedUsers == 0 && room.ServerToken == "",
                "wrong password leaves no reservation or server launch even for creator"
            );
        }

        room.State = Smart_Training_Server.Enum.ServerState.Starting;
        room.ServerToken = "fixture";
        rooms.RegisterServer(room.Id, "fixture", "password-server");
        var admission = await Join(owner, password);
        Check(
            admission.Success && rooms.ConfirmConnection(room.Id, "password-server", owner.Id, admission.AdmissionToken).Success,
            "correct password reserves and confirms ordinary admission token"
        );
        Check(
            !(await Join(member, "wrong")).Success && room.Users.Count == 1 && member.PendingRoomId == "",
            "incorrect participant password preserves connected membership"
        );
        var pending = await Join(member, password);
        Check(pending.Success, "correct participant password receives reservation");
        UpdateRoomPasswordRequest Update(string? value) => new()
        {
            LobbyId = lobby.Id,
            RoomId = room.Id,
            Password = value
        };
        var originalHash = room.PasswordHash.ToArray();
        Check(
            !rooms.UpdateRoomPassword(member.Id, Update("member-pass")).Success,
            "password update rejects reservation participant"
        );
        foreach (var invalid in new string? []
        {
            null,
            "abc",
            "    ",
            new string ('x', 33)
        }

        )
        {
            Check(
                !rooms.UpdateRoomPassword(owner.Id, Update(invalid)).Success && room.PasswordHash.SequenceEqual(originalHash) && rooms.ValidateConnection(room.Id, "password-server", member.Id, pending.AdmissionToken).Success,
                "invalid password update preserves existing password and reservation"
            );
        }

        var wrongLobby = Update("new-password");
        wrongLobby.LobbyId = "wrong";
        Check(!rooms.UpdateRoomPassword(owner.Id, wrongLobby).Success, "password update rejects mismatched lobby");
        Check(
            rooms.UpdateRoomPassword(owner.Id, Update("new-password")).Success && room.HasPassword && member.PendingRoomId == "" && room.ReservedUsers == 0 && owner.CurrentRoomId == room.Id,
            "password update cancels only pending admissions and preserves connected user"
        );
        Check(
            !rooms.ConfirmConnection(room.Id, "password-server", member.Id, pending.AdmissionToken).Success && rooms.ConfirmConnection(room.Id, "password-server", owner.Id, admission.AdmissionToken).Success,
            "changed password invalidates old reservation but not confirmed connection token"
        );
        Check(!(await Join(member, password)).Success, "old password rejected after change");
        var fresh = await Join(member, "new-password");
        Check(
            fresh.Success && rooms.ConfirmConnection(room.Id, "password-server", member.Id, fresh.AdmissionToken).Success,
            "new password admits participant after change"
        );
        rooms.TransferRoomMaster(owner.Id, new() {
                LobbyId = lobby.Id,
                RoomId = room.Id,
                TargetUserId = member.Id
            });
        Check(
            !rooms.UpdateRoomPassword(owner.Id, Update("former-pass")).Success,
            "former master loses password authority immediately"
        );
        room.IsRunning = true;
        room.IsEntryLocked = true;
        Check(
            rooms.UpdateRoomPassword(member.Id, Update("")).Success && !room.HasPassword && room.PasswordHash.Length == 0 && room.PasswordSalt.Length == 0 && room.IsRunning && room.IsEntryLocked && room.Users.Count == 2,
            "password removal clears secrets without changing phase, lock or connected users"
        );
        Check(
            rooms.UpdateRoomPassword(member.Id, Update("new-password")).Success,
            "current master can set password on public room"
        );
        rooms.ServerDisconnected(room.Id, "password-server");
        Check(
            room.HasPassword && !(await Join(owner, null)).Success && owner.PendingRoomId == "",
            "server reset preserves password and reconnect cannot bypass it"
        );
    }
}
