using TMPro;
using UnityEngine;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// The turn clock: an analog clock face with a minute hand and a second hand, showing the time the player has
    /// left. The hands turn clockwise from 12; the minute hand goes round once an hour, so a 4-hour or 1-day limit
    /// shows its last hour's minutes once it is within the hour. Optional digits under it.
    /// </summary>
    public class MpClock : MonoBehaviour {
        [Tooltip("Turned about its pivot (put the pivot at the face's centre).")]
        public RectTransform minuteHand;
        public RectTransform secondHand;
        [Tooltip("The time left as digits (m:ss). Optional.")]
        public TMP_Text digits;
        [Tooltip("The face turns this colour in the last minute. Optional.")]
        public UnityEngine.UI.Image face;
        public Color normal = new Color(0.93f, 0.94f, 0.96f, 1f);
        public Color hurry = new Color(0.95f, 0.45f, 0.35f, 1f);

        /// <summary>Show <paramref name="seconds"/> left (none below 0).</summary>
        public void Show(double seconds) {
            double s = System.Math.Max(0.0, seconds);
            float sec = (float)(s % 60.0), min = (float)((s / 60.0) % 60.0);
            if (secondHand != null) secondHand.localEulerAngles = new Vector3(0f, 0f, -6f * sec);
            if (minuteHand != null) minuteHand.localEulerAngles = new Vector3(0f, 0f, -6f * min);
            if (digits != null) {
                int whole = (int)System.Math.Ceiling(s);
                digits.text = whole >= 3600 ? (whole / 3600) + ":" + (whole / 60 % 60).ToString("00") + ":" + (whole % 60).ToString("00") : (whole / 60) + ":" + (whole % 60).ToString("00");
            }
            if (face != null) face.color = s <= 60.0 ? hurry : normal;
        }
    }
}
