using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HexGenerator : MonoBehaviour
{

    public HexTile hextile;

    public float off;

    public int width;

    public HexTile[,,] Grid;

    public List<HexTile> tiles;
    // Start is called before the first frame update

    public static HexGenerator hg;

    public static void forAllTiles(Func<HexTile,int> func) {
        foreach(HexTile t in hg.tiles) {
            func(t);
        }
    }

    public HexTile get(int x, int y, int z)
    {
        // Off the board: null. (Bounds checked here: catching the out-of-range exception cost an
        // exception, with its stack trace, for every lookup past the edge, and the AI makes
        // thousands of them a turn near the edges.)
        if (Grid == null) return null;
        int i = x + width / 2, j = y + width / 2, k = z + width / 2;
        if (i < 0 || j < 0 || k < 0 || i >= Grid.GetLength(0) || j >= Grid.GetLength(1) || k >= Grid.GetLength(2)) return null;
        return Grid[i, j, k];
    }
    /*
    public void load(BoardState bs)
    {
        width = (int)Mathf.Pow(bs.grid.Length, 1 / 3f);
        Grid = new HexTile[width, width, width];

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < width; j++)
            {
                for (int k = 0; k < width; k++)
                {
                    if(bs.grid[i,j,k] == 1)
                    {
                        int t = (i - (i & 1)) / 2 + k;
                        float offset = 0;
                        if (i % 2 == 0)
                            offset += extentsy;
                        HexTile go =
                            GameObject.Instantiate(hextile, (Vector2)transform.position + new Vector2(i * extentsx * 2 * 1 / 1.5f, t * extentsy * 2 - offset), Quaternion.identity, transform);
                        Grid[i,j,k] = go;
                        Grid[i, j, k].x = i - width / 2;
                        Grid[i, j, k].y = j - width / 2;
                        Grid[i, j, k].z = k - width / 2;
                        go.type = bs.grid[i,j,k] - 1;
                    }
                }
            }
                
        }

        neighborup();
    }*/

    internal void Clear()
    {
        foreach (HexTile t in GetComponentsInChildren<HexTile>())
        {
            Destroy(t.gameObject);
        }

        Grid = null;
    }

    void Awake()
    {
        hg = this;
        Grid = new HexTile[width, width,width];
         
        for (int i = -width; i < width; i++)
        {
            for (int j = -width; j < width; j++)
            {
                //for (int k = -width; k < width; k++) {
                    HexTile go = null;
                    try {
                        float offset = 0;
                        if (i % 2 == 0)
                            offset += 3.25f;//.display.sprite.bounds.extents.y;

                        int x = i;// i - (j + (j & 1)) / 2;
                        int z = j - (i - (i & 1)) / 2;
                        int y = -x - z;

                        if (Grid[x + width / 2, y + width / 2, z + width / 2] == null) {
                            float extentsx = 4;
                            float extentsy = 3.25f;

                            //float lat = 3 / 2f * extentsx * x;
                            //float lon = -Mathf.Sqrt(3) * extentsy * (y / 2 + z);

                            go =
                                GameObject.Instantiate(hextile, (Vector2)transform.position + new Vector2(i * extentsx * 2 * 1 / 1.5f, j * extentsy * 2 - offset), Quaternion.identity, transform);
                            Grid[x + width / 2, y + width / 2, z + width / 2] = go;
                            Grid[x + width / 2, y + width / 2, z + width / 2].x = x;
                            Grid[x + width / 2, y + width / 2, z + width / 2].y = y;
                            Grid[x + width / 2, y + width / 2, z + width / 2].z = z;
                            //go.type = Math.Min(UnityEngine.Random.Range(0,3)/2,(Math.Abs(x * y + z) % 2));
                            tiles.Add(go);
                        }
                    } catch (IndexOutOfRangeException e) {
                        if (go != null)
                            Destroy(go.gameObject);
                    }
                //}
            }
        }
        neighborup();
    }

    void neighborup()
    {
        for (int i = -width; i < width; i++)
        {
            for (int j = -width; j < width; j++)
            {
                for (int k = -width; k < width; k++)
                {
                    if (get(i, j, k))
                    {
                        if (get(i - 1, j, k + 1))
                        {
                            get(i, j, k).neighbors.Add(get(i - 1, j, k + 1));
                        }
                        if (get(i, j - 1, k + 1))
                        {
                            get(i, j, k).neighbors.Add(get(i, j - 1, k + 1));
                        }
                        if (get(i - 1, j + 1, k))
                        {
                            get(i, j, k).neighbors.Add(get(i - 1, j + 1, k));
                        }
                        if (get(i + 1, j, k - 1))
                        {
                            get(i, j, k).neighbors.Add(get(i + 1, j, k - 1));
                        }
                        if (get(i, j + 1, k - 1))
                        {
                            get(i, j, k).neighbors.Add(get(i, j + 1, k - 1));
                        }
                        if (get(i + 1, j - 1, k))
                        {
                            get(i, j, k).neighbors.Add(get(i + 1, j - 1, k));
                        }
                    }
                }
            }
        }
    }

    public delegate void withFunction(HexTile t);

    public void all(withFunction func)
    {
        for (int i = -width; i < width; i++)
        {
            for (int j = -width; j < width; j++)
            {
                for (int k = -width; k < width; k++)
                {
                    if(get(i, j, k))
                        func(get(i,j,k));
                }
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        /*
        foreach (Transform child in transform)
        {
            GameObject.Destroy(child.gameObject);
        }
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                float offset = 0;
                if (j % 2 == 0)
                    offset += extentsx * off;
                GameObject.Instantiate(hextile, (Vector2)transform.position + new Vector2(i * extentsx * 2 * 1.5f + offset, j * extentsy), Quaternion.identity, transform);
            }
        }
        */
    }
}
