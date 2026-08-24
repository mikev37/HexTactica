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
        upwards = Mathf.Lerp(upwards, (Mathf.PingPong(Time.time + offset, bobdistance) - bobdistance/2) * speed,pullspeed);
        //upwards = Mathf.Lerp(upwards, Mathf.Sin(Time.timeSinceLevelLoad * speed + Mathf.PI * offset * 2),  pullspeed);
        if(bobdistance > 0)
            transform.localPosition = position + Vector3.up * upwards * (bobdistance - bobdistance/2); 
    }
}