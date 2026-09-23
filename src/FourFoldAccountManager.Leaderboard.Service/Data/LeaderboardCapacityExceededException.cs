namespace FourFoldAccountManager.Leaderboard.Service.Data;

public sealed class LeaderboardCapacityExceededException(string message) : Exception(message);
