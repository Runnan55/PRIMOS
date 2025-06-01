using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class AllowedEmailManager : MonoBehaviour
{
    private HashSet<string> allowedEmails = new HashSet<string>();

    private void Awake()
    {
        LoadAllowedEmails();
    }

    private void LoadAllowedEmails()
    {
        TextAsset emailListFile = Resources.Load<TextAsset>("AllowedEmails");
        if (emailListFile != null)
        {
            string[] emails = emailListFile.text.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var email in emails)
            {
                allowedEmails.Add(email.Trim().ToLower());
            }
            Debug.Log($"Allowed emails cargados: {allowedEmails.Count}");
        }
        else
        {
            Debug.LogError("No se encontró el archivo AllowedEmails.txt en Resources.");
        }
    }

    public bool IsEmailAllowed(string email)
    {
        return allowedEmails.Contains(email.Trim().ToLower());
    }
}
