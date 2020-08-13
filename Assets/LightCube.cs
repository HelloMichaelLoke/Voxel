using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LightCube : MonoBehaviour
{
    public World world;

    private float brightness;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 offset = Vector3.up * this.GetComponent<BoxCollider>().size.y;
        float brightnessA = this.world.GetLightValue(this.transform.position);
        float brightnessB = this.world.GetLightValue(this.transform.position + offset);
        float brightnessC = this.world.GetLightValue(this.transform.position + offset / 2.0f);
        brightness = Mathf.Lerp(this.brightness, Mathf.Max(Mathf.Max(brightnessA, brightnessB), brightnessC), 10.0f * Time.deltaTime);
        brightness = Mathf.Clamp(brightness, 0.1f, 1.0f);
        this.GetComponent<MeshRenderer>().material.SetFloat("_Brightness", brightness);
    }
}
