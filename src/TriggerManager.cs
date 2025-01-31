using System;
using System.Runtime;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HKTimer.Triggers;

namespace HKTimer {
    public class TriggerManager : MonoBehaviour {

        public TimeSpan pb { get; set; } = TimeSpan.Zero;
        public TimeSpan pbDelta { get; set; } = TimeSpan.Zero;
        public TimeSpan avg { get; set; } = TimeSpan.Zero;

        public int avgAmount = 3;
        public List<TimeSpan> pbAvg = new();
        public bool runningSegment { get; set; } = false;

        public TriggerPlaceType triggerPlaceType { get; set; } = TriggerPlaceType.Collision;

        private List<Trigger> triggers = new List<Trigger>();
        // Special start and end triggers you can place
        // only these ones will get changed using the keybinds
        private Trigger start;
        private Trigger end;

        private GameObject pbDisplay;
        private GameObject pbStaticTextDisplay;
        private GameObject pbDeltaDisplay;
        private GameObject avgDisplay;
        private GameObject avgStaticTextDisplay;
        private GameObject roomDeltaDisplay;

        public Dictionary<string, Type> triggerTypes { get; } = new Dictionary<string, Type>()
        {
            { CollisionTrigger.name, typeof(CollisionTrigger) },
            { MovementTrigger.name, typeof(MovementTrigger) },
            { SceneTrigger.name, typeof(SceneTrigger) }
        };

        // MonoBehaviours
        public Timer timer { get; private set; }

        public void TimerReset() {
            this.runningSegment = false;
            this.pbDeltaDisplay.SetActive(false);
        }

        public void InitDisplay() {
            if(this.timer != null && this.timer.timerCanvas != null) {
                var timerCanvas = this.timer.timerCanvas;
                CanvasUtil.CreateFonts();
                this.pb = TimeSpan.Zero;
                this.pbDisplay = CanvasUtil.CreateTextPanel(
                    timerCanvas,
                    this.PbText(),
                    HKTimer.settings.textSize * 3 / 4,
                    TextAnchor.MiddleRight,
                    Timer.CreateTimerRectData(new Vector2(190, 30), new Vector2(-50, -45))
                );
                this.avgDisplay = CanvasUtil.CreateTextPanel(
                    timerCanvas,
                    this.AvgText(),
                    HKTimer.settings.textSize / 2,
                    TextAnchor.LowerLeft,
                    Timer.CreateTimerRectData(new Vector2(120, 20), new Vector2(-50, -95))
                );
                this.avgStaticTextDisplay = CanvasUtil.CreateTextPanel(
                    timerCanvas,
                    "avg " + avgAmount,
                    HKTimer.settings.textSize / 2,
                    TextAnchor.MiddleRight,
                    Timer.CreateTimerRectData(new Vector2(90, 30), new Vector2(0, -95))
                );
                this.pbStaticTextDisplay = CanvasUtil.CreateTextPanel(
                    timerCanvas,
                    "PB",
                    HKTimer.settings.textSize * 3 / 4,
                    TextAnchor.MiddleRight,
                    Timer.CreateTimerRectData(new Vector2(50, 30), new Vector2(0, -45))
                );
                this.pbDeltaDisplay = CanvasUtil.CreateTextPanel(
                    timerCanvas,
                    this.PbDeltaText(),
                    HKTimer.settings.textSize / 2,
                    TextAnchor.MiddleRight,
                    Timer.CreateTimerRectData(new Vector2(120, 20), new Vector2(-50, -70))
                );
                this.pbDeltaDisplay.SetActive(false);
                this.roomDeltaDisplay = CanvasUtil.CreateTextPanel(
                    timerCanvas,
                    this.PbDeltaText(),
                    HKTimer.settings.textSize / 2,
                    TextAnchor.MiddleRight,
                    Timer.CreateTimerRectData(new Vector2(120, 20), new Vector2(-180, -45))
                );
                this.roomDeltaDisplay.SetActive(false);
            } else {
                Modding.Logger.LogError(
                    "[HKTimer] Timer canvas is null, not creating trigger display" 
                );
            }
        }

        private GameObject displayedAlert;
        public void ShowAlert(string text) {
            if(this.displayedAlert != null) this.displayedAlert.SetActive(false);
            var canvas = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceOverlay, 100);
            var tp = CanvasUtil.CreateTextPanel(
                canvas,
                text,
                20,
                TextAnchor.LowerRight,
                new CanvasUtil.RectData(
                    new Vector2(1920, 80),
                    new Vector2(-40, 40),
                    new Vector2(1, 0),
                    new Vector2(1, 0),
                    new Vector2(1, 0)
                )
            );
            GameObject.DontDestroyOnLoad(canvas);
            var cg = tp.AddComponent<CanvasGroup>();
            this.displayedAlert = tp;
            this.StartCoroutine(this.AnimateAlert(cg, canvas));
        }
        
        // slightly modified code to allow overriding a fade
        private static IEnumerator FadeInCanvasGroup(CanvasGroup cg) {
            float loopFailsafe = 0f;
            cg.alpha = 0f;
            cg.gameObject.SetActive(true);
            while(cg.alpha < 1f) {
                cg.alpha += Time.unscaledDeltaTime * 3.2f;
                loopFailsafe += Time.unscaledDeltaTime;
                if(cg.alpha >= 0.95f) {
                    cg.alpha = 1f;
                    break;
                }

                if(loopFailsafe >= 2f) {
                    break;
                }

                yield return null;
            }

            cg.alpha = 1f;
            cg.interactable = true;
            yield return null;
        }
        private static IEnumerator FadeOutCanvasGroup(CanvasGroup cg) {
            float loopFailsafe = 0f;
            cg.interactable = false;
            while(cg.alpha > 0.05f) {
                cg.alpha -= Time.unscaledDeltaTime * 3.2f;
                loopFailsafe += Time.unscaledDeltaTime;
                if(cg.alpha <= 0.05f) {
                    break;
                }

                if(loopFailsafe >= 2f) {
                    break;
                }

                yield return null;
            }

            cg.alpha = 0f;
            yield return null;
        }

        private IEnumerator AnimateAlert(CanvasGroup cg, GameObject canvas) {
            yield return FadeInCanvasGroup(cg);
            yield return new WaitForSeconds(3f);
            yield return FadeOutCanvasGroup(cg);
            GameObject.DestroyImmediate(canvas);
        }

        public TriggerManager Initialize(Timer timer) {
            this.timer = timer;
            this.timer.OnTimerReset += TimerReset;
            return this;
        }

        private string PbText() {
            return string.Format(
                "{0}:{1:D2}.{2:D3}",
                Math.Floor(this.pb.TotalMinutes),
                this.pb.Seconds,
                this.pb.Milliseconds
            );
        }
        private string PbDeltaText() {
            var dur = this.pbDelta.Duration();
            return string.Format(
                "{0}{1}:{2:D2}.{3:D3}",
                this.pbDelta < TimeSpan.Zero ? "-" : "+",
                Math.Floor(dur.TotalMinutes),
                dur.Seconds,
                dur.Milliseconds
            );
        }

        public void roomDeltaText(TimeSpan time)
        {
            var dur = time.Duration();
            roomDeltaDisplay.GetComponent<Text>().text = string.Format(
                "{0}{1}:{2:D2}.{3:D3}",
                this.pbDelta < TimeSpan.Zero ? "-" : "+",
                Math.Floor(dur.TotalMinutes),
                dur.Seconds,
                dur.Milliseconds
            );
        }

        private string AvgText()
        {
            return string.Format(
                "{0}:{1:D2}.{2:D3}",
                Math.Floor(this.avg.TotalMinutes),
                this.avg.Seconds,
                this.avg.Milliseconds
            );
        }
        public void removeLastAvg()
        {
            pbAvg.RemoveAt(pbAvg.Count - 1);
            avg = pbAvg.Count > avgAmount - 1 ? calcAvg(this.avgAmount) : TimeSpan.Zero;
            avgDisplay.GetComponent<Text>().text = AvgText();
        }

        public void UpdateAvgAmountText()
        {
            this.avgStaticTextDisplay.GetComponent<Text>().text = "avg " + this.avgAmount.ToString();
            avg = pbAvg.Count > avgAmount - 1 ? calcAvg(this.avgAmount) : TimeSpan.Zero;
            avgDisplay.GetComponent<Text>().text = AvgText();
        }

        private TimeSpan calcAvg(int amount)
        {
            TimeSpan temp = TimeSpan.Zero;
            for (int i = pbAvg.Count - amount; i < pbAvg.Count; i++)
            {
                temp += pbAvg[i];
            }
            return divide(temp,amount);
        }

        private TimeSpan divide(TimeSpan dividend, int divisor)
        {
            double temp = dividend.TotalSeconds / divisor;
            return TimeSpan.FromSeconds(temp);
        }

        private void writeAvgToFile()
        {
            string finalString = "";
            foreach (TimeSpan temp in pbAvg)
            {
                finalString += string.Format(
                                    "{0}:{1:D2}.{2:D3}",
                                    Math.Floor(temp.TotalMinutes),
                                    temp.Seconds,
                                    temp.Milliseconds
                                ) + "\n";
            }
            try
            {
                File.WriteAllText(HKTimer.HKTimerPath + "/avgTimes.txt", finalString);
            }
            catch (Exception e)
            {
                HKTimer.instance.LogError(e);
            }
            
        }

        public void UpdatePB() {
            var avgText = this.avgDisplay.GetComponent<Text>();
            var pbText = this.pbDisplay.GetComponent<Text>();
            var pbDeltaText = this.pbDeltaDisplay.GetComponent<Text>();
            var time = this.timer.time;
            pbAvg.Add(time);
            //HKTimer.instance.Log(pbAvg.Count + " Current times:");
            //foreach(TimeSpan temp in pbAvg)
            //{
            //    HKTimer.instance.Log(temp);
            //}
            writeAvgToFile();
            
            if (pbAvg.Count > avgAmount - 1)
            {
                // HKTimer.instance.Log("Avg: " + calcAvg(avgAmount));
                avg = calcAvg(avgAmount);
                avgText.text = this.AvgText();
            }
            if(this.pb == null || this.pb == TimeSpan.Zero) {
                this.avg = TimeSpan.Zero;
                avgText.text = this.AvgText();
                this.pb = time;
                pbText.text = this.PbText();
                this.pbDelta = TimeSpan.Zero;
                this.pbDeltaDisplay.SetActive(false);
            } else if(this.pb > time) {
                this.pbDelta = time - this.pb;
                this.pb = time;
                pbText.text = this.PbText();
                pbDeltaText.text = this.PbDeltaText();
                this.pbDeltaDisplay.SetActive(true);
            } else {
                this.pbDelta = time - this.pb;
                pbDeltaText.text = this.PbDeltaText();
                this.pbDeltaDisplay.SetActive(true);
            }
        }

        public void ResetPB() {
            pbAvg = new();
            this.avg = TimeSpan.Zero;
            this.avgDisplay.GetComponent<Text>().text = this.AvgText();

            this.pb = TimeSpan.Zero;
            this.pbDeltaDisplay.SetActive(false);
            this.pbDisplay.GetComponent<Text>().text = this.PbText();
        }

        public void SpawnTriggers() {
            this.start?.Spawn(this);
            this.end?.Spawn(this);
            this.triggers?.ForEach(x => x.Spawn(this));
        }

        public void Awake() {
            OnLogicPreset += (string p, ref bool s) => {
                var o = true;
                switch(p) {
                    case "segment_start":
                        if(this.timer.state == Timer.TimerState.STOPPED) {
                            // HKTimer.instance.timer.roomCounter = 0;
                            this.timer.ResetTimer();
                            this.timer.StartTimer();
                            this.runningSegment = true;
                        }
                        break;
                    case "segment_end":
                        if(this.timer.state != Timer.TimerState.STOPPED && this.runningSegment) {
                            this.UpdatePB();
                            this.timer.PauseTimer();
                            this.runningSegment = false;
                        }
                        break;
                    case "timer_restart":
                        this.timer.RestartTimer();
                        break;
                    case "timer_reset":
                        this.timer.ResetTimer();
                        break;
                    case "timer_pause":
                        this.timer.PauseTimer();
                        break;
                    case "timer_start":
                        this.timer.StartTimer();
                        break;
                    default:
                        o = false;
                        break;
                }
                if(o) s = true;
            };
        }
        public void Start() {
            Modding.Logger.Log("[HKTimer] Started target manager");
            LoadTriggers();
        }
        public void Update() {
            if(StringInputManager.GetKeyDown(HKTimer.settings.set_start)) {
                this.start?.Destroy(this);
                switch(this.triggerPlaceType) {
                    case TriggerPlaceType.Collision:
                        this.start = new CollisionTrigger() {
                            scene = GameManager.instance.sceneName,
                            logic = new JValue("segment_start"),
                            color = "green",
                            start = HeroController.instance.transform.position - new Vector3(0.1f, 0.1f),
                            end = HeroController.instance.transform.position + new Vector3(0.1f, 0.1f),
                        };
                        break;
                    case TriggerPlaceType.Movement:
                        this.start = new MovementTrigger() {
                            scene = GameManager.instance.sceneName,
                            logic = new JValue("segment_start")
                        };
                        if(this.end != null) this.end.logic = new JArray {
                            this.end.logic,
                            JObject.FromObject(new {
                                type = "command",
                                command = "enable",
                                trigger = new JValue("start"),
                                data = new JObject()
                            })
                        };
                        this.ShowAlert("Created movement trigger");
                        break;
                    case TriggerPlaceType.Scene:
                        this.start = new SceneTrigger() {
                            scene = GameManager.instance.sceneName,
                            logic = new JValue("segment_start")
                        };
                        this.ShowAlert("Created scene trigger");
                        break;
                }
                this.start.Spawn(this);
                pbAvg = new();
                this.avg = TimeSpan.Zero;
                this.avgDisplay.GetComponent<Text>().text = this.AvgText();

                this.pb = TimeSpan.Zero;
                this.pbDisplay.GetComponent<Text>().text = this.PbText();
            }
            if(StringInputManager.GetKeyDown(HKTimer.settings.set_end)) {
                this.end?.Destroy(this);
                switch(this.triggerPlaceType) {
                    case TriggerPlaceType.Collision:
                        this.end = new CollisionTrigger() {
                            scene = GameManager.instance.sceneName,
                            logic = new JValue("segment_end"),
                            color = "red",
                            start = HeroController.instance.transform.position - new Vector3(0.1f, 0.1f),
                            end = HeroController.instance.transform.position + new Vector3(0.1f, 0.1f),
                        };
                        break;
                    case TriggerPlaceType.Movement:
                        this.end = null;
                        this.ShowAlert("Cannot create movement trigger as end");
                        return;
                    case TriggerPlaceType.Scene:
                        this.end = new SceneTrigger() {
                            scene = GameManager.instance.sceneName,
                            logic = new JValue("segment_end")
                        };
                        this.ShowAlert("Created scene trigger");
                        break;
                };
                this.end.Spawn(this);
                this.ResetPB();
            }
        }
        public void OnDestroy() {
            this.start?.Destroy(this);
            this.end?.Destroy(this);
            this.triggers?.ForEach(x => x.Destroy(this));
            GameObject.Destroy(this.pbDeltaDisplay);
            GameObject.Destroy(this.pbDisplay);
            if(this.timer != null) this.timer.OnTimerReset -= TimerReset;
        }

        public void LoadTriggers() {
            try {
                Modding.Logger.Log("[HKTimer] Loading triggers");
                if(File.Exists(HKTimer.HKTimerPath + "/triggers.json")) {
                    var triggers = JsonConvert.DeserializeObject<TriggerSaveFile>(File.ReadAllText(
                        HKTimer.HKTimerPath + "/triggers.json"
                    ));
                    // Destroy all triggers
                    this.triggers?.ForEach(x => x.Destroy(this));
                    this.start?.Destroy(this);
                    this.end?.Destroy(this);

                    this.triggers = triggers.other?.ConvertAll<Trigger>(x => x.ToTrigger(this)) ?? new List<Trigger>();
                    this.start = triggers.start?.ToTrigger(this);
                    this.end = triggers.end?.ToTrigger(this);
                    this.pbDisplay.GetComponent<Text>().text = this.PbText();
                    this.SpawnTriggers();
                }
            } catch(Exception e) {
                Modding.Logger.LogError(e);
            }
        }
        public void SaveTriggers() {
            try {
                Modding.Logger.Log("[HKTimer] Saving triggers");
                File.WriteAllText(
                    HKTimer.HKTimerPath + "/triggers.json",
                    JsonConvert.SerializeObject(
                        new TriggerSaveFile() {
                            pb_ticks = this.pb.Ticks,
                            start = this.start == null ? null : TriggerSave.FromTrigger(this.start),
                            end = this.end == null ? null : TriggerSave.FromTrigger(this.end),
                            other = this.triggers?.ConvertAll(
                                x => x == null ? null : TriggerSave.FromTrigger(x)
                            ) ?? new List<TriggerSave>(),
                        },
                        Formatting.Indented
                    )
                );
            } catch(Exception e) {
                Modding.Logger.LogError(e);
            }
        }

        public enum TriggerPlaceType {
            Collision,
            Movement,
            Scene
        }

        public delegate void LogicPresetDelegate(string preset, ref bool successful);
        public event LogicPresetDelegate OnLogicPreset;

        public void ExecLogic(JToken logic) {
            switch(logic) {
                case JArray v:
                    // Array of logic items, handle each in order
                    foreach(var c in v) {
                        ExecLogic(c);
                    }
                    break;
                case JValue v:
                    // Specific logic preset
                    if(v.Value is string s) {
                        var successful = false;
                        OnLogicPreset.Invoke(s, ref successful);
                        if(!successful) {
                            Modding.Logger.LogError("[HKTimer] Invalid logic preset `" + s + "`");
                        }
                    } else {
                        Modding.Logger.LogError("[HKTimer] Invalid logic `" + v.ToString() + "`");
                    }
                    break;
                case JObject v:
                    if(v["type"] is JValue { Value: "command" }) {
                        try {
                            var lcmd = v.ToObject<LogicCommand>();
                            Trigger trigger;
                            if(lcmd.trigger is JValue { Value: "start" }) {
                                trigger = this.start;
                            } else if(lcmd.trigger is JValue { Value: "end" }) {
                                trigger = this.end;
                            } else {
                                trigger = this.triggers[lcmd.trigger.ToObject<int>()];
                            }
                            trigger.TriggerCommand(lcmd.command, lcmd.data);
                        } catch(Exception e) {
                            Modding.Logger.LogError(e);
                        }
                    }
                    break;
                default:
                    Modding.Logger.LogError("[HKTimer] Invalid logic `" + logic.ToString() + "`");
                    break;
            }
        }

        private class LogicCommand {
            public string command = default;
            public JValue trigger = default;
            public JObject data = default;
        }
    }

    public class TriggerSaveFile {
        public long pb_ticks;
        public List<TriggerSave> other;
        public TriggerSave start;
        public TriggerSave end;
    }

    public class TriggerSave {
        public string trigger_type;
        public JObject trigger_data;


        public Trigger ToTrigger(TriggerManager tm) {
            Type ty;
            if(tm.triggerTypes.TryGetValue(this.trigger_type, out ty)) {
                return (Trigger) trigger_data.ToObject(ty);
            } else {
                throw new Exception("Unknown trigger type '" + this.trigger_type + "'");
            }
        }

        public static TriggerSave FromTrigger(Trigger x) {
            return new TriggerSave() {
                trigger_data = JObject.FromObject(x),
                trigger_type = x.Name
            };
        }
    }

    public abstract class Trigger {
        public JToken logic;
        public string scene;

        [JsonIgnore]
        public abstract string Name {
            get;
        }
        public abstract void Spawn(TriggerManager tm);
        public abstract void Destroy(TriggerManager tm);

        public virtual void TriggerCommand(string command, JObject data) { }
    }


    public class JsonVec2Converter : JsonConverter {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            var vec = (Vector2) value;
            JObject j = new JObject { { "x", vec.x }, { "y", vec.y } };
            j.WriteTo(writer);
        }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            return existingValue;
        }
        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanConvert(Type objectType) {
            return objectType == typeof(Vector2);
        }
    }
}