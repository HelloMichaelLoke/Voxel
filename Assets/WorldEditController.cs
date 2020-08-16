using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldEditController : MonoBehaviour
{
    public World world;
    public Camera mainCamera;
    public GameObject visualizer;
    public GameObject visualizerClosest;

    void Start()
    {
        
    }

    void Update()
    {
        RaycastHit hit;
        Ray ray = this.mainCamera.ScreenPointToRay(Input.mousePosition);
        int layerMask = 1 << 8;

        if (Physics.Raycast(ray, out hit, 120.0f, layerMask))
        {
            Transform objectHit = hit.transform;

            if (objectHit.tag == "Terrain")
            {
                this.visualizer.SetActive(true);
                this.visualizer.transform.position = hit.point;

                if (Vector3.Distance(hit.point, this.transform.position) >= 2.0f)
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        this.world.WorldEditDraw(hit.point, (sbyte)-128, (byte)3);
                    }
                    else if (Input.GetMouseButtonDown(1))
                    {
                        this.world.WorldEditErase(hit.point);
                    }
                }

                //this.visualizerClosest.SetActive(true);
                //this.visualizerClosest.transform.position = worldPosition;
            }
        }
        else
        {
            this.visualizer.SetActive(false);
            this.visualizerClosest.SetActive(false);
        }
    }
}
