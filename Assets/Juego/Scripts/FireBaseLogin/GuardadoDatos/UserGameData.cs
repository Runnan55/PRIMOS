using System.ComponentModel;
using UnityEngine;

[System.Serializable]
public class UserGameData
{
    public string playerName;
    //public string Skin;
    //public int Points;
    //A�adir m�s campos luego

    public UserGameData(string name)
    {
        playerName = name;
        //selectedSkin = "default";
        //points = 0;
    }
}
