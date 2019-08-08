// refrence http://wiki.unity3d.com/index.php/MirrorReflection4

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// This is in fact just the Water script from Pro Standard Assets,
// just with refraction stuff removed.

// Make mirror live-update even when not in play mode
public class MirrorReflection : MonoBehaviour
{
    const string ReflectionTexParamStr = "_ReflectionTex";
    static int nestCountMax = 5;
    private static int nestCount;


    public bool m_DisablePixelLights = true;
    public int m_TextureSize = 256;
    public float m_ClipPlaneOffset = 0.07f;

    public LayerMask m_ReflectLayers = -1;

    Dictionary<Camera, (Camera, RenderTexture)> mirrorDatas = new Dictionary<Camera, (Camera, RenderTexture)>();



    Material mat;

    private void Awake()
    {
        mat = GetComponent<Renderer>()?.material;
    }

    Stack<Texture> texs = new Stack<Texture>();
    private void OnRenderObject()
    {
        if (texs.Any())
        {
            var tex = texs.Pop();
            mat.SetTexture(ReflectionTexParamStr, tex);
        }
        //Debug.Log($"End [{Camera.current.name}] {name} ");
    }

    // This is called when it's known that the object will be rendered by some
    // camera. We render reflections and do other updates here.
    // Because the script executes in edit mode, reflections for the scene view
    // camera will just work!
    public void OnWillRenderObject()
    {
        if (!enabled || (mat == null))
            return;

        Camera cam = Camera.current;
        if (cam == null)
            return;


        //Debug.Log($"[{cam.name}] {name} {nestCount}");

        Texture reflectionTex = Texture2D.blackTexture;
        // Safeguard from recursive reflections.        
        if (nestCount <= nestCountMax)
        {
            nestCount++;

            // find out the reflection plane: position and normal in world space
            Vector3 pos = transform.position;
            //Vector3 normal = transform.up;
            Vector3 normal = transform.forward;

            var camTrans = cam.transform;
            var cull = Vector3.Dot(camTrans.position - pos, normal) >= 0f;
            if (!cull)
            {
                var (refCam, reftex) = GetMirrorData(cam);


                // Optionally disable pixel lights for reflection
                int oldPixelLightCount = QualitySettings.pixelLightCount;
                if (m_DisablePixelLights)
                    QualitySettings.pixelLightCount = 0;

                UpdateCameraModes(cam, refCam);

                // Render reflection
                // Reflect camera around reflection plane
                float d = -Vector3.Dot(normal, pos) - m_ClipPlaneOffset;
                Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

                Matrix4x4 reflection = Matrix4x4.zero;
                CalculateReflectionMatrix(ref reflection, reflectionPlane);
                refCam.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

                // Setup oblique projection matrix so that near plane is our reflection
                // plane. This way we clip everything below/above it for free.
                Vector4 clipPlane = CameraSpacePlane(refCam, pos, normal, 1.0f);
                Matrix4x4 projection = cam.CalculateObliqueMatrix(clipPlane);
                refCam.projectionMatrix = projection;

                refCam.cullingMask = ~(1 << 4) & m_ReflectLayers.value; // never render water layer
                refCam.targetTexture = reftex;

                GL.invertCulling = nestCount % 2 == 1;

                refCam.Render();

                GL.invertCulling = !GL.invertCulling;


                // Restore pixel light count
                if (m_DisablePixelLights)
                    QualitySettings.pixelLightCount = oldPixelLightCount;

                reflectionTex = reftex;
            }


            nestCount--;
        }

        if (nestCount > 0) texs.Push(mat.GetTexture(ReflectionTexParamStr));
        mat.SetTexture(ReflectionTexParamStr, reflectionTex);
    }


    // Cleanup all the objects we possibly have created
    void OnDisable()
    {
        mirrorDatas.Values.ToList().ForEach(data =>
        {
            DestroyImmediate(data.Item1);
            DestroyImmediate(data.Item2);
        });
    }


    private void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null)
            return;
        // set camera to clear the same way as current camera
        dest.clearFlags = src.clearFlags;
        dest.backgroundColor = src.backgroundColor;
        if (src.clearFlags == CameraClearFlags.Skybox)
        {
            Skybox sky = src.GetComponent(typeof(Skybox)) as Skybox;
            Skybox mysky = dest.GetComponent(typeof(Skybox)) as Skybox;
            if (!sky || !sky.material)
            {
                mysky.enabled = false;
            }
            else
            {
                mysky.enabled = true;
                mysky.material = sky.material;
            }
        }
        // update other values to match current camera.
        // even if we are supplying custom camera&projection matrices,
        // some of values are used elsewhere (e.g. skybox uses far plane)
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
    }

    // On-demand create any objects we need
    private (Camera, RenderTexture) GetMirrorData(Camera cam)
    {
        mirrorDatas.TryGetValue(cam, out var data);
        var (refCam, refTex) = data;

        // Reflection render texture
        if (refTex == null || refTex.width != m_TextureSize || refTex.height != m_TextureSize)
        {
            if (refTex != null)
                DestroyImmediate(refTex);
            refTex = new RenderTexture(m_TextureSize, m_TextureSize, 16);
            refTex.name = "__MirrorReflection" + GetInstanceID();
            refTex.isPowerOfTwo = true;
            refTex.hideFlags = HideFlags.DontSave;
        }

        // Camera for reflection
        if (refCam == null) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
        {
            GameObject go = new GameObject("Mirror Refl Camera id" + GetInstanceID() + " for " + cam.GetInstanceID(), typeof(Camera), typeof(Skybox));
            refCam = go.GetComponent<Camera>();
            refCam.enabled = false;
            refCam.transform.position = transform.position;
            refCam.transform.rotation = transform.rotation;
            //reflectionCamera.gameObject.AddComponent("FlareLayer");
            go.hideFlags = HideFlags.HideAndDontSave;
        }

        data = (refCam, refTex);
        mirrorDatas[cam] = data;

        return data;
    }

    // Given position/normal of the plane, calculates plane in camera space.
    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * m_ClipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = -m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    // Calculates reflection matrix around the given plane
    private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
        reflectionMat.m01 = (-2F * plane[0] * plane[1]);
        reflectionMat.m02 = (-2F * plane[0] * plane[2]);
        reflectionMat.m03 = (-2F * plane[3] * plane[0]);

        reflectionMat.m10 = (-2F * plane[1] * plane[0]);
        reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
        reflectionMat.m12 = (-2F * plane[1] * plane[2]);
        reflectionMat.m13 = (-2F * plane[3] * plane[1]);

        reflectionMat.m20 = (-2F * plane[2] * plane[0]);
        reflectionMat.m21 = (-2F * plane[2] * plane[1]);
        reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
        reflectionMat.m23 = (-2F * plane[3] * plane[2]);

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }
}