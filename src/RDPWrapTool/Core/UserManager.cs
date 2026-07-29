using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RDPWrapTool.Core;

/// <summary>
/// Manages Windows user accounts - creation, deletion, group membership.
/// </summary>
public class UserManager
{
    public event Action<string>? OnLog;
    private void Log(string msg) => OnLog?.Invoke(msg);

    /// <summary>
    /// Create a new Windows user account.
    /// </summary>
    public bool CreateUser(string username, string password, string? fullName = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Log("[-] Username and password cannot be empty.");
            return false;
        }

        // Check if user already exists
        if (UserExists(username))
        {
            Log($"[-] User '{username}' already exists.");
            return false;
        }

        try
        {
            // Use net user command for simplicity and reliability
            string args = $"user \"{username}\" \"{password}\" /add";
            if (!string.IsNullOrEmpty(fullName))
                args += $" /fullname:\"{fullName}\"";

            var result = RunCommand("net.exe", args);
            if (result.success)
            {
                Log($"[+] User '{username}' created successfully.");

                // Add to Remote Desktop Users group
                AddToRdpUsersGroup(username);
                return true;
            }
            else
            {
                Log($"[-] Failed to create user: {result.output}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"[-] CreateUser error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Delete a Windows user account.
    /// </summary>
    public bool DeleteUser(string username)
    {
        if (!UserExists(username))
        {
            Log($"[-] User '{username}' does not exist.");
            return false;
        }

        try
        {
            var result = RunCommand("net.exe", $"user \"{username}\" /delete");
            if (result.success)
            {
                Log($"[+] User '{username}' deleted successfully.");
                return true;
            }
            else
            {
                Log($"[-] Failed to delete user: {result.output}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"[-] DeleteUser error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Change user password.
    /// </summary>
    public bool ChangePassword(string username, string newPassword)
    {
        if (!UserExists(username))
        {
            Log($"[-] User '{username}' does not exist.");
            return false;
        }

        try
        {
            var result = RunCommand("net.exe", $"user \"{username}\" \"{newPassword}\"");
            if (result.success)
            {
                Log($"[+] Password changed for user '{username}'.");
                return true;
            }
            else
            {
                Log($"[-] Failed to change password: {result.output}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"[-] ChangePassword error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Add user to Remote Desktop Users group.
    /// </summary>
    public bool AddToRdpUsersGroup(string username)
    {
        try
        {
            var result = RunCommand("net.exe", $"localgroup \"Remote Desktop Users\" \"{username}\" /add");
            if (result.success)
            {
                Log($"[+] User '{username}' added to 'Remote Desktop Users' group.");
                return true;
            }
            else
            {
                Log($"[-] Failed to add user to RDP group: {result.output}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"[-] AddToRdpUsersGroup error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove user from Remote Desktop Users group.
    /// </summary>
    public bool RemoveFromRdpUsersGroup(string username)
    {
        try
        {
            var result = RunCommand("net.exe", $"localgroup \"Remote Desktop Users\" \"{username}\" /delete");
            if (result.success)
            {
                Log($"[+] User '{username}' removed from 'Remote Desktop Users' group.");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log($"[-] RemoveFromRdpUsersGroup error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a user exists.
    /// </summary>
    public bool UserExists(string username)
    {
        var users = ListUsers();
        foreach (var u in users)
        {
            if (u.Equals(username, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// List all local user accounts.
    /// </summary>
    public List<string> ListUsers()
    {
        var users = new List<string>();
        try
        {
            var result = RunCommand("net.exe", "user");
            if (!result.success) return users;

            // Parse net user output - users are listed between dashed lines
            var lines = result.output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inUserList = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("--"))
                {
                    inUserList = !inUserList;
                    continue;
                }
                if (inUserList && !line.Contains("The command completed"))
                {
                    // Each line may contain multiple usernames
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (!string.IsNullOrWhiteSpace(part) && part != "The")
                            users.Add(part);
                    }
                }
            }
        }
        catch { }
        return users;
    }

    /// <summary>
    /// List members of Remote Desktop Users group.
    /// </summary>
    public List<string> ListRdpUsers()
    {
        var users = new List<string>();
        try
        {
            var result = RunCommand("net.exe", "localgroup \"Remote Desktop Users\"");
            if (!result.success) return users;

            var lines = result.output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inUserList = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("--"))
                {
                    inUserList = !inUserList;
                    continue;
                }
                if (inUserList && !line.Contains("The command completed"))
                {
                    users.Add(line.Trim());
                }
            }
        }
        catch { }
        return users;
    }

    private (bool success, string output) RunCommand(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.Default
            };
            using var proc = Process.Start(psi);
            if (proc == null) return (false, "Process.Start returned null");
            proc.WaitForExit(10000);
            string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            return (proc.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
