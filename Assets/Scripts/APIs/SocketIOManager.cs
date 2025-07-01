using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using DG.Tweening;
using System.Linq;
using Newtonsoft.Json;
using Best.SocketIO;
using Best.SocketIO.Events;
using Newtonsoft.Json.Linq;
using System.Runtime.Serialization;
using Best.HTTP.Shared;

public class SocketIOManager : MonoBehaviour
{
    [SerializeField]
    private SlotBehaviour slotManager;

    [SerializeField]
    private UIManager uiManager;

    internal GameData initialData = null;
    internal UiData initUIData = null;
    internal Root resultData = null;
    internal Player playerdata = null;
    [SerializeField]
    internal List<string> bonusdata = null;
    //WebSocket currentSocket = null;
    internal bool isResultdone = false;

    protected string nameSpace = "playground"; //BackendChanges
    private Socket gameSocket; //BackendChanges

    private SocketManager manager;

    [SerializeField] internal JSFunctCalls JSManager;

    [SerializeField]
    internal JSHandler _jsManager;

    protected string SocketURI = null;
    // protected string TestSocketURI = "https://game-crm-rtp-backend.onrender.com/";
    protected string TestSocketURI = "https://mx2md3l5-5000.inc1.devtunnels.ms/";
    // protected string TestSocketURI = "https://fuzzy-space-happiness-6jv566jpv6jhrw5v-5000.app.github.dev/";
    // protected string TestSocketURI = "https://6m8kh503-5000.inc1.devtunnels.ms/";


    [SerializeField]
    private string testToken;

    protected string gameID = "SL-FLC";
    // protected string gameID = "";

    internal bool isLoaded = false;

    internal bool SetInit = false;

    private const int maxReconnectionAttempts = 6;
    private readonly TimeSpan reconnectionDelay = TimeSpan.FromSeconds(10);

    private void Awake()
    {
        //Debug.unityLogger.logEnabled = false;
        isLoaded = false;
        SetInit = false;

    }

    private void Start()
    {
        //OpenWebsocket();
        OpenSocket();
    }

    //void ReceiveAuthToken(string jsonData)
    //{
    //    Debug.Log("Received data: " + jsonData);

    //    // Parse the JSON data
    //    var data = JsonUtility.FromJson<AuthTokenData>(jsonData);
    //    SocketURI = data.socketURL;
    //    myAuth = data.cookie;
    //    nameSpace = data.nameSpace; //BackendChanges

    //    // Proceed with connecting to the server using myAuth and socketURL
    //}

    string myAuth = null;

    private void OpenSocket()
    {
        // Create and setup SocketOptions
        SocketOptions options = new SocketOptions();
        options.ReconnectionAttempts = maxReconnectionAttempts;
        options.ReconnectionDelay = reconnectionDelay;
        options.Reconnection = true;
        options.ConnectWith = Best.SocketIO.Transports.TransportTypes.WebSocket; //BackendChanges

#if UNITY_WEBGL && !UNITY_EDITOR
    string url = Application.absoluteURL;
    Debug.Log("Unity URL : " + url);
    ExtractUrlAndToken(url);

    Func<SocketManager, Socket, object> webAuthFunction = (manager, socket) =>
    {
      return new
      {
        token = testToken,
      };
    };
    options.Auth = webAuthFunction;
#else
        Func<SocketManager, Socket, object> authFunction = (manager, socket) =>
        {
            return new
            {
                token = testToken,
            };
        };
        options.Auth = authFunction;
#endif
        // Proceed with connecting to the server
        SetupSocketManager(options);

    }

    public void ExtractUrlAndToken(string fullUrl)
    {
        Uri uri = new Uri(fullUrl);
        string query = uri.Query; // Gets the query part, e.g., "?url=http://localhost:5000&token=e5ffa84216be4972a85fff1d266d36d0"

        Dictionary<string, string> queryParams = new Dictionary<string, string>();
        string[] pairs = query.TrimStart('?').Split('&');

        foreach (string pair in pairs)
        {
            string[] kv = pair.Split('=');
            if (kv.Length == 2)
            {
                queryParams[kv[0]] = Uri.UnescapeDataString(kv[1]);
            }
        }

        if (queryParams.TryGetValue("url", out string extractedUrl) &&
            queryParams.TryGetValue("token", out string token))
        {
            Debug.Log("Extracted URL: " + extractedUrl);
            Debug.Log("Extracted Token: " + token);
            testToken = token;
            SocketURI = extractedUrl;
        }
        else
        {
            Debug.LogError("URL or token not found in query parameters.");
        }
    }
    private IEnumerator WaitForAuthToken(SocketOptions options)
    {
        // Wait until myAuth is not null
        while (myAuth == null)
        {
            Debug.Log("My Auth is null");
            yield return null;
        }
        while (SocketURI == null)
        {
            Debug.Log("My Socket is null");
            yield return null;
        }
        Debug.Log("My Auth is not null");
        // Once myAuth is set, configure the authFunction
        Func<SocketManager, Socket, object> authFunction = (manager, socket) =>
        {
            return new
            {
                token = myAuth,
                gameId = gameID
            };
        };
        options.Auth = authFunction;

        Debug.Log("Auth function configured with token: " + myAuth);

        // Proceed with connecting to the server
        SetupSocketManager(options);
    }

    private void SetupSocketManager(SocketOptions options)
    {
        // Create and setup SocketManager
#if UNITY_EDITOR
        this.manager = new SocketManager(new Uri(TestSocketURI), options);
#else
        this.manager = new SocketManager(new Uri(SocketURI), options);
#endif
        if (string.IsNullOrEmpty(nameSpace))
        {  //BackendChanges Start
            gameSocket = this.manager.Socket;
        }
        else
        {
            print("nameSpace: " + nameSpace);
            gameSocket = this.manager.GetSocket("/" + nameSpace);
        }
        // Set subscriptions
        gameSocket.On<ConnectResponse>(SocketIOEventTypes.Connect, OnConnected);
        gameSocket.On<string>(SocketIOEventTypes.Disconnect, OnDisconnected);
        gameSocket.On<string>(SocketIOEventTypes.Error, OnError);
        gameSocket.On<string>("game:init", OnListenEvent);
        gameSocket.On<string>("spin:result", OnResult);
        gameSocket.On<bool>("socketState", OnSocketState);
        gameSocket.On<string>("internalError", OnSocketError);
        gameSocket.On<string>("alert", OnSocketAlert);
        gameSocket.On<string>("AnotherDevice", OnSocketOtherDevice); //BackendChanges Finish
    }

    // Connected event handler implementation
    void OnConnected(ConnectResponse resp)
    {
        Debug.Log("Connected!");
        SendPing();
    }

    private void OnDisconnected(string response)
    {
        Debug.Log("Disconnected from the server");
        //if (maxReconnectionAttempts <= this.manager.ReconnectAttempts)
        //{
        StopAllCoroutines();
        uiManager.DisconnectionPopup(false);
        //}
        //else
        //{
        //    uiManager.DisconnectionPopup(false);
        //}
    }

    private void OnError(string response)
    {
        Debug.LogError("Error: " + response);
    }

    private void OnListenEvent(string data)
    {
        // Debug.Log("Received some_event with data: " + data);
        ParseResponse(data);
    }

    private void OnSocketState(bool state)
    {
        if (state)
        {
            Debug.Log("my state is " + state);
        }
        else
        {

        }
    }
    private void OnSocketError(string data)
    {
        Debug.Log("Received error with data: " + data);
    }
    private void OnSocketAlert(string data)
    {
        Debug.Log("Received alert with data: " + data);
    }
    void OnResult(string data)
    {
        ParseResponse(data);
    }
    private void OnSocketOtherDevice(string data)
    {
        Debug.Log("Received Device Error with data: " + data);
        uiManager.ADfunction();
    }

    private void SendPing()
    {
        InvokeRepeating("AliveRequest", 0f, 3f);
    }

    private void AliveRequest()
    {
        SendDataWithNamespace("YES I AM ALIVE");
    }

    private void SendDataWithNamespace(string eventName, string json = null)
    {
        // Send the message
        if (gameSocket != null && gameSocket.IsOpen) //BackendChanges
        {
            if (json != null)
            {
                gameSocket.Emit(eventName, json);
                Debug.Log("JSON data sent: " + json);
            }
            else
            {
                gameSocket.Emit(eventName);
            }
        }
        else
        {
            Debug.LogWarning("Socket is not connected.");
        }
    }



    internal void CloseSocket()
    {
        SendDataWithNamespace("EXIT");
    }

    internal void ReactNativeCallOnFailedToConnect() //BackendChanges
    {
#if UNITY_WEBGL && !UNITY_EDITOR
    JSManager.SendCustomMessage("onExit");
#endif
    }

    private void ParseResponse(string jsonObject)
    {
       
        Debug.Log(jsonObject);
        Root myData = JsonConvert.DeserializeObject<Root>(jsonObject);
       

        string id = myData.id;

        switch (id)
        {
            case "initData":
                {
                   
                    playerdata = myData.player;                    
                    initUIData = myData.uiData;                    
                    initialData = myData.gameData;                    
                    
                    
                    // bonusdata = GetBonusData(myData.gameData.spinBonus);

                    if (!SetInit)
                    {
                        List<string> LinesString = ConvertListListIntToListString(initialData.lines);
                        PopulateSlotSocket();
                        SetInit = true;
                    }
                    else
                    {
                        RefreshUI();
                    }
                    break;
                }
            case "ResultData":
                {
                    // Debug.Log(jsonObject);
                    // myData.message.GameData.FinalResultReel = ConvertListOfListsToStrings(myData.message.GameData.ResultReel);
                    //myData.message.GameData.FinalsymbolsToEmit = TransformAndRemoveRecurring(myData.message.GameData.symbolsToEmit);
                    resultData = myData;
                    playerdata = myData.player;
                    isResultdone = true;
                    Debug.Log("TestData:" + "P22222");
                    break;
                }
            case "ExitUser":
                {
                    if (this.manager != null) //BackendChanges
                    {
                        Debug.Log("Dispose my Socket");
                        this.manager.Close();
                    }
                    // Application.ExternalCall("window.parent.postMessage", "onExit", "*");
#if UNITY_WEBGL && !UNITY_EDITOR
                        JSManager.SendCustomMessage("onExit");
#endif
                    break;
                }
        }
    }

    void DebugTest()
    {
        Debug.Log("TestData:" + initialData.lines.Count);
        Debug.Log("TestData:" + initialData.bets.Count);
        Debug.Log("TestData:" + initialData.freespinOptions.Count);
        Debug.Log("TestData:" + "");
        Debug.Log("TestData:" + "");
        Debug.Log("TestData:" + "");
    }
    private void RefreshUI()
    {
        uiManager.InitialiseUIData(initUIData.paylines);
    }

    private void PopulateSlotSocket()
    {
        slotManager.shuffleInitialMatrix();
        //for (int i = 0; i < LineIds.Count; i++)
        //{
        //    slotManager.FetchLines(LineIds[i], i);
        //}

        slotManager.SetInitialUI();

        isLoaded = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        JSManager.SendCustomMessage("OnEnter");
#endif
    }

    internal void AccumulateResult(double currBet)
    {
        isResultdone = false;
        MessageData message = new MessageData();
        message.currentBet = slotManager.BetCounter;
        // Serialize message data to JSON
        string json = JsonUtility.ToJson(message);
        SendDataWithNamespace("spin:request", json);
    }
    internal void AcumulateFreeSpin(int selectedButtonindex)
    {
        var message = new { type = "FREESPINOPTION",  option = selectedButtonindex };
        string json = JsonConvert.SerializeObject(message);
        SendDataWithNamespace("freespin:request", json);
        Debug.Log("Dev_Test:  " + json);
        Debug.Log(JsonConvert.SerializeObject(message));

    }
    private List<string> RemoveQuotes(List<string> stringList)
    {
        for (int i = 0; i < stringList.Count; i++)
        {
            stringList[i] = stringList[i].Replace("\"", ""); // Remove inverted commas
        }
        return stringList;
    }

    private List<string> ConvertListListIntToListString(List<List<int>> listOfLists)
    {
        List<string> resultList = new List<string>();

        foreach (List<int> innerList in listOfLists)
        {
            // Convert each integer in the inner list to string
            List<string> stringList = new List<string>();
            foreach (int number in innerList)
            {
                stringList.Add(number.ToString());
            }

            // Join the string representation of integers with ","
            string joinedString = string.Join(",", stringList.ToArray()).Trim();
            resultList.Add(joinedString);
        }

        return resultList;
    }

    private List<string> ConvertListOfListsToStrings(List<List<string>> inputList)
    {
        List<string> outputList = new List<string>();

        foreach (List<string> row in inputList)
        {
            string concatenatedString = string.Join(",", row);
            outputList.Add(concatenatedString);
        }

        return outputList;
    }

    private List<string> TransformAndRemoveRecurring(List<List<string>> originalList)
    {
        // Flattened list
        List<string> flattenedList = new List<string>();
        foreach (List<string> sublist in originalList)
        {
            flattenedList.AddRange(sublist);
        }

        // Remove recurring elements
        HashSet<string> uniqueElements = new HashSet<string>(flattenedList);

        // Transformed list
        List<string> transformedList = new List<string>();
        foreach (string element in uniqueElements)
        {
            transformedList.Add(element.Replace(",", ""));
        }

        return transformedList;
    }
}

[Serializable]
public class freeSpinSelect
{
    public string id;
    public data option;
}
[Serializable]
public class data
{

    public int option;
}

[Serializable]
public class BetData
{
    public double currentBet;
    public double currentLines;
    public double spins;
}

[Serializable]
public class AuthData
{
    public string GameID;
    //public double TotalLines;
}
[Serializable]
public class AuthTokenData
{
    public string cookie;
    public string socketURL;
    public string nameSpace; //BackendChanges
}

[Serializable]
public class MessageData
{
    public int currentBet;
}

[Serializable]
public class ExitData
{
    public string id;
}

[Serializable]
public class InitData
{
    public AuthData Data;
    public string id;
}
[Serializable]
public class Jackpot
{
    public bool isTriggered ;
    public double amount ;
}
[Serializable]
public class Scatter
{
    public List<Value> values { get; set; }
}

[Serializable]
public class Value
{
    public int value { get; set; }
    public List<int> index { get; set; }
}

[Serializable]
public class Payload
{
    public double winAmount ;
    public List<Win> wins ;
}
[Serializable]
public class Win
{
    public int line ;
    public List<int> positions ;
    public double amount ;
}

[Serializable]
public class Root
{
    //Result Data
    public bool success ;
    public List<List<string>> matrix ;
    public string name ;
    public Payload payload ;
    public Bonus bonus ;
    public Jackpot jackpot ;
    public Scatter scatter ;   
    public Features features { get; set; }
    //Initial Data
    public string id ;
    public GameData gameData ;
    public UiData uiData ;
    public Player player;


}

[Serializable]
public class Features
{
    public FreeSpin freeSpin { get; set; }
    public Scatter scatter { get; set; }

    public List<FreespinOption> freespinOptions { get; set; }
    public List<int> jackpotMultipliers { get; set; }
    public List<BonusTrigger> bonusTrigger { get; set; }
}
[Serializable]
public class UiData
{
    public Paylines paylines ;
}
[Serializable]
public class Player
{
    public double balance ;
}
public class GameData
{
    public List<List<int>> lines { get; set; }
    public List<double> bets { get; set; }
    public List<FreespinOption> freespinOptions { get; set; }
    public List<int> jackpotMultipliers { get; set; }
    public List<BonusTrigger> bonusTrigger { get; set; }
}
[Serializable]
public class FreespinOption
{
    public int count ;
    public List<int> multiplier ;
}

[Serializable]
public class BonusTrigger
{
    public List<int> count ;
    public int rows ;
}
[Serializable]
public class Bonus
{
    public List<List<int>> matrix ;
    public bool isTriggered ;
    public int scatterCount ;
    public int spinCount ;
}
[Serializable]
public class FreeSpin
{
    public bool isFreeSpin { get; set; }
    public int count { get; set; }
}

[Serializable]
public class ScatterValue
{
    public double value ;
    public List<int> index ;
}

[Serializable]
public class Message
{
    public GameData GameData ;
    public UIData UIData ;
    public PlayerData PlayerData ;
    public List<string> BonusData ;
}


[Serializable]
public class UIData
{
    public Paylines paylines ;
    public List<string> spclSymbolTxt ;

    public string ToULink ;
    public string PopLink ;
}

[Serializable]
public class Paylines
{
    public List<Symbol> symbols ;
}
[Serializable]
public class Symbol
{
    public int id { get; set; }
    public string name { get; set; }
    public List<int> multiplier { get; set; }
    public string description { get; set; }
}


[Serializable]
public class PlayerData
{
    public double Balance ;
    public double haveWon ;
    public double currentWining ;
}


