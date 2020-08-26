using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ShowFPS : MonoBehaviour
{
    float timer = 0.0f;
    int frameCount = 0;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        timer += Time.deltaTime;
        frameCount++;
        if (timer >= 1.0f)
        {
            this.GetComponent<Text>().text = "FPS: " + frameCount.ToString();
            frameCount = 0;
            timer = 0.0f;
        }
    }
}
