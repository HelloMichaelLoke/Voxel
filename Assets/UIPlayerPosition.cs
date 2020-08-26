using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public class UIPlayerPosition : MonoBehaviour
{
    public World world;
    
    public Transform playerTransform;
    public Transform visualizerClosestTransform;

    private Text textComponent;

    private void Start()
    {
        this.textComponent = this.GetComponent<Text>();
    }

    private void Update()
    {
        Vector3 playerPosition = this.playerTransform.position;
        this.textComponent.text = "[Player]\n";
        this.textComponent.text += "World Position: " + this.GetWorldPosition(playerPosition) + "\n";
        this.textComponent.text += "Chunk Position: " + this.GetChunkPosition(playerPosition) + "\n";
        this.textComponent.text += "Relative Position " + this.GetRelativePosition(playerPosition) + "\n";

        Vector3 visualizerClosestPosition = this.visualizerClosestTransform.position;
        this.textComponent.text += "[Visualizer Closest]\n";
        this.textComponent.text += "World Position: " + this.GetWorldPosition(visualizerClosestPosition) + "\n";
        this.textComponent.text += "Chunk Position: " + this.GetChunkPosition(visualizerClosestPosition) + "\n";
        this.textComponent.text += "Relative Position " + this.GetRelativePosition(visualizerClosestPosition) + "\n";
    }

    private string GetWorldPosition(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x);
        int z = Mathf.FloorToInt(position.z);

        return "[X: " + x.ToString() + " | Z: " + z.ToString() + "]";
    }

    private string GetChunkPosition(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x / 16.0f);
        int z = Mathf.FloorToInt(position.z / 16.0f);

        return "[X: " + x.ToString() + " | Z: " + z.ToString() + "]";
    }

    private string GetRelativePosition(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x % 16.0f);
        if (x < 0) x += 16;
        int z = Mathf.FloorToInt(position.z % 16.0f);
        if (z < 0) z += 16;

        return "[X: " + x.ToString() + " | Z: " + z.ToString() + "]";
    }
}
