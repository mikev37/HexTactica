using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BobUpDown : MonoBehaviour
{
    Vector3 position;
    public float bobdistance;
    public float speed = 1;
    public float pullspeed = 1;
    [Range(0,1)]
    public float offset = 0;
    // Start is called before the first frame update
    void Start()
    {
        position = transform.localPosition;
    }

    private float upwards = 0;
    // Update is called once per frame
    void Update()
    {
        // Mathf.PingPong with a length of 0 is NaN (0/0), and a NaN once in upwards stayed there: a
        // unit whose bob distance was 0 on its first frame (just loaded, or animated up from 0) gave
        // a NaN local position. Only bob with a distance, and start again from rest after anything odd.
        if (bobdistance <= 0) return;
        if (float.IsNaN(upwards) || float.IsInfinity(upwards)) upwards = 0;
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)) position = Vector3.zero;
        upwards = Mathf.Lerp(upwards, (Mathf.PingPong(Time.time + offset, bobdistance) - bobdistance/2) * speed,pullspeed);
        //upwards = Mathf.Lerp(upwards, Mathf.Sin(Time.timeSinceLevelLoad * speed + Mathf.PI * offset * 2),  pullspeed);
        Vector3 p = position + Vector3.up * upwards * (bobdistance - bobdistance/2);
        if (!float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)) transform.localPosition = p;
    }
}