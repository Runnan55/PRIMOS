using UnityEngine;
using System.Collections.Generic;

public class MatchInfo
{
    public string matchId;
    public string mode;
    public CustomRoomPlayer admin;
    public List<CustomRoomPlayer> players = new List<CustomRoomPlayer>();
    public bool isStarted;
    public string sceneName;
}
