// link: https://towardsdatascience.com/image-analysis-for-beginners-creating-a-motion-detector-with-opencv-4ca6faba4b42
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.VideoioModule;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MotionDetection : MonoBehaviour
{
    VideoWriter writer;
    VideoCapture capture;
    Mat rgbMat;
    Texture2D texture;
    bool isPlaying = false;
    bool shouldUpdateVideoFrame = false;
    public string videoFileName;

    private MatOfRect humans;
    private MatOfDouble weights;
    private Size winStride;

    Mat recordingFrameRgbMat;
    bool isRecording;
    public string savePath;

    private Size kSize;
    private double sigmaX;
    private List<MatOfPoint> contours;

    // Start is called before the first frame update
    void Start()
    {
        capture = new VideoCapture();
        capture.open(Utils.getFilePath(videoFileName));

        writer = new VideoWriter();
        writer.open(savePath, VideoWriter.fourcc('D', 'V', 'I', 'X'), 30, new Size(640, 480));

        if (!writer.isOpened())
        {
            Debug.LogError("writer.isOpened() false");
            writer.release();
        }

        Initialize();
    }

    private void Initialize()
    {
        rgbMat = new Mat();

        if (!capture.isOpened())
        {
            Debug.LogError(videoFileName + " is not opened. Please move to �Assets/StreamingAssets/� folder.");
        }

        //Debug.Log("CAP_PROP_FORMAT: " + capture.get(Videoio.CAP_PROP_FORMAT));
        //Debug.Log("CAP_PROP_POS_MSEC: " + capture.get(Videoio.CAP_PROP_POS_MSEC));
        //Debug.Log("CAP_PROP_POS_FRAMES: " + capture.get(Videoio.CAP_PROP_POS_FRAMES));
        //Debug.Log("CAP_PROP_POS_AVI_RATIO: " + capture.get(Videoio.CAP_PROP_POS_AVI_RATIO));
        //Debug.Log("CAP_PROP_FRAME_COUNT: " + capture.get(Videoio.CAP_PROP_FRAME_COUNT));
        //Debug.Log("CAP_PROP_FPS: " + capture.get(Videoio.CAP_PROP_FPS));
        //Debug.Log("CAP_PROP_FRAME_WIDTH: " + capture.get(Videoio.CAP_PROP_FRAME_WIDTH));
        //Debug.Log("CAP_PROP_FRAME_HEIGHT: " + capture.get(Videoio.CAP_PROP_FRAME_HEIGHT));
        //double ext = capture.get(Videoio.CAP_PROP_FOURCC);
        //Debug.Log("CAP_PROP_FOURCC: " + (char)((int)ext & 0XFF) + (char)(((int)ext & 0XFF00) >> 8) + (char)(((int)ext & 0XFF0000) >> 16) + (char)(((int)ext & 0XFF000000) >> 24));

        humans = new MatOfRect();
        weights = new MatOfDouble();
        winStride = new Size(8, 8);

        kSize = new Size(5, 5);
        sigmaX = 0;
        contours = new List<MatOfPoint>();

        capture.grab();
        capture.retrieve(rgbMat);
        Imgproc.resize(rgbMat, rgbMat, new Size(640, 480));
        int frameWidth = rgbMat.cols();
        int frameHeight = rgbMat.rows();
        Debug.Log("frame width: " + frameWidth + " frame height: " + frameHeight);
        texture = new Texture2D(frameWidth, frameHeight, TextureFormat.RGB24, false);
        //gameObject.transform.localScale = new Vector3((float)frameWidth, (float)frameHeight, 1);
        //float widthScale = (float)Screen.width / (float)frameWidth;
        //float heightScale = (float)Screen.height / (float)frameHeight;
        //if (widthScale < heightScale)
        //{
        //    Camera.main.orthographicSize = ((float)frameWidth * (float)Screen.height / (float)Screen.width) / 2;
        //}
        //else
        //{
        //    Camera.main.orthographicSize = (float)frameHeight / 2;
        //}
        capture.set(Videoio.CAP_PROP_POS_FRAMES, 0);

        gameObject.GetComponent<RawImage>().texture = texture;

        StartCoroutine(WaitFrameTime());

        isPlaying = true;
        StartCoroutine(MotionDetectionCoroutine());
    }

    // Update is called once per frame
    IEnumerator MotionDetectionCoroutine()
    {
        double videoFPS = (capture.get(Videoio.CAP_PROP_FPS) <= 0) ? 10.0 : capture.get(Videoio.CAP_PROP_FPS);
        float frameTime_sec = (float)(1000.0 / videoFPS / 1000.0);
        WaitForSeconds wait = new WaitForSeconds(frameTime_sec);

        while (true)
        {
            
            if (isPlaying && shouldUpdateVideoFrame)
            {
                shouldUpdateVideoFrame = false;

                //Loop play
                if (capture.get(Videoio.CAP_PROP_POS_FRAMES) >= capture.get(Videoio.CAP_PROP_FRAME_COUNT))
                    capture.set(Videoio.CAP_PROP_POS_FRAMES, 0);

                if (capture.grab())
                {
                    capture.retrieve(rgbMat);

                    // 1. Load image, convert to RGB
                    Imgproc.cvtColor(rgbMat, rgbMat, Imgproc.COLOR_BGR2RGB);
                    Imgproc.resize(rgbMat, rgbMat, new Size(640, 480));

                    // 2. prepare image: grayscale and blur
                    Mat prevFrame = new Mat();
                    Imgproc.cvtColor(rgbMat, prevFrame, Imgproc.COLOR_BGR2GRAY);
                    Imgproc.GaussianBlur(prevFrame, prevFrame, kSize, sigmaX);

                    yield return wait; // wait till end of frame

                    Mat prepFrame = null;
                    if (capture.grab())
                    {
                        prepFrame = new Mat();
                        capture.retrieve(prepFrame);
                        Imgproc.cvtColor(prepFrame, prepFrame, Imgproc.COLOR_BGR2RGB);
                        Imgproc.resize(prepFrame, prepFrame, new Size(640, 480));
                        Imgproc.cvtColor(prepFrame, prepFrame, Imgproc.COLOR_BGR2GRAY);
                        Imgproc.GaussianBlur(prepFrame, prepFrame, kSize, sigmaX);
                    }

                    if(prepFrame == null)
                    {
                        yield return wait;
                        continue;
                    }

                    // 3. calculate difference
                    Mat diffFrame = new Mat();
                    Core.absdiff(prevFrame, prepFrame, diffFrame);

                    // 4. Dilute the image a bit to make differences more seeable; more suitable for contour detection
                    Imgproc.dilate(diffFrame, diffFrame, Mat.ones(5, 5, CvType.CV_8UC1), new Point(-1, -1), 1);

                    // 5. Only take different areas that are different enough (>20 / 255)
                    Mat threshFrame = new Mat();
                    Imgproc.threshold(diffFrame, threshFrame, 20, 255, Imgproc.THRESH_BINARY);

                    // finding areas and contouring
                    Mat srcHierarchy = new Mat();
                    contours.Clear();
                    Imgproc.findContours(threshFrame, contours, srcHierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);

                    // find the coordinates of the areas
                    foreach(MatOfPoint contour in contours)
                    {
                        //if(Imgproc.contourArea(contour) < 100)
                        //{
                        //    // too small, skip
                        //    continue;
                        //}

                        OpenCVForUnity.CoreModule.Rect rect = Imgproc.boundingRect(contour);
                        Imgproc.rectangle(rgbMat, rect, new Scalar(0, 255, 0), 2);
                    }

                    Utils.matToTexture2D(rgbMat, texture);

                    if (isRecording)
                    {
                        Imgproc.cvtColor(rgbMat, rgbMat, Imgproc.COLOR_RGB2BGR);
                        Core.flip(rgbMat, rgbMat, 0);
                        writer.write(rgbMat);
                    }

                    yield return wait;
                }
            }
            else
            {
                yield return null;
            }
        }
    }

    private IEnumerator WaitFrameTime()
    {
        double videoFPS = (capture.get(Videoio.CAP_PROP_FPS) <= 0) ? 10.0 : capture.get(Videoio.CAP_PROP_FPS);
        float frameTime_sec = (float)(1000.0 / videoFPS / 1000.0);
        WaitForSeconds wait = new WaitForSeconds(frameTime_sec);
        //prevFrameTickCount = currentFrameTickCount = Core.getTickCount();

        capture.grab();

        while (true)
        {
            if (isPlaying)
            {
                shouldUpdateVideoFrame = true;
                isRecording = true;
                //prevFrameTickCount = currentFrameTickCount;
                //currentFrameTickCount = Core.getTickCount();

                yield return wait;
            }
            else
            {
                yield return null;
            }
        }
    }

    void OnDestroy()
    {
        StopCoroutine(MotionDetectionCoroutine());
        StopCoroutine(WaitFrameTime());

        capture.release();

        if (rgbMat != null)
            rgbMat.Dispose();

        if (writer != null && !writer.IsDisposed)
        {
            writer.release();
        }
        isPlaying = false;
        isRecording = false;
    }
}
