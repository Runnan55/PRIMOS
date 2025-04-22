using UnityEngine;
using System.Collections.Generic;

public class MatchInfo
{
    public string matchId;
    public string mode;
    public bool isStarted;
    public string sceneName;

    public CustomRoomPlayer admin;
    public List<CustomRoomPlayer> players = new List<CustomRoomPlayer>();

    public MatchInfo() { }

    public MatchInfo(string matchId, string mode, bool isStarted)
    {
        this.matchId = matchId;
        this.mode = mode;
        this.isStarted = isStarted;
    }

}
