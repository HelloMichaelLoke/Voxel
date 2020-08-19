using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SunCircle : MonoBehaviour
{
    float time = 0.0f;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        time += 0.05f * Time.deltaTime;
        time = time % 1.0f;


        if (time >= 0.5f) time = Mathf.Clamp(time + 0.1f * Time.deltaTime, 0.5f, 1.0f);

        if (time >= 0.0f && time <= 0.25f) this.GetComponent<Light>().intensity = time / 0.25f;
        else if (time > 0.25f && time <= 0.5f) this.GetComponent<Light>().intensity = 1.0f - ((time - 0.25f) / 0.25f);
        else this.GetComponent<Light>().intensity = 0.0f;

        Vector3 eulerAngles = this.transform.eulerAngles;
        eulerAngles.Set(time * 360.0f, 0.0f, 0.0f);
        this.transform.localEulerAngles = eulerAngles;
    }
}
