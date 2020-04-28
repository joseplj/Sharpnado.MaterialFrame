﻿//------------------------------------------------------------------------------
//
// https://github.com/Dimezis/BlurView
// Latest commit a955a76 on 4 Nov 2019
//
// Copyright 2016 Dmitry Saviuk
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//------------------------------------------------------------------------------
// Adapted to csharp by Jean-Marie Alfonsi
//------------------------------------------------------------------------------
// <auto-generated/>

using System;

using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Runtime;
using Android.Views;

namespace Sharpnado.MaterialFrame.Droid.BlurView
{
    /**
 * Blur Controller that handles all blur logic for the attached View.
 * It honors View size changes, View animation and Visibility changes.
 * <p>
 * The basic idea is to draw the view hierarchy on a bitmap, excluding the attached View,
 * then blur and draw it on the system Canvas.
 * <p>
 * It uses {@link ViewTreeObserver.OnPreDrawListener} to detect when
 * blur should be updated.
 * <p>
 * Blur is done on the main thread.
 */
    public class BlockingBlurController : IBlurViewFacade
    {

        // Bitmap size should be divisible by ROUNDING_VALUE to meet stride requirement.
        // This will help avoiding an extra bitmap allocation when passing the bitmap to RenderScript for blur.
        // Usually it's 16, but on Samsung devices it's 64 for some reason.
        private const int ROUNDING_VALUE = 64;

        private const int TRANSPARENT = 0;

        private const float DEFAULT_SCALE_FACTOR = 8f;

        private const float DEFAULT_BLUR_RADIUS = 16f;

        private const float scaleFactor = DEFAULT_SCALE_FACTOR;

        private float blurRadius = DEFAULT_BLUR_RADIUS;

        private float roundingWidthScaleFactor = 1f;

        private float roundingHeightScaleFactor = 1f;

        private RenderScriptBlur blurAlgorithm;

        private Canvas internalCanvas;

        private Bitmap internalBitmap;

        private readonly View blurView;

        private int overlayColor;

        private readonly ViewGroup rootView;

        private readonly int[] rootLocation = new int[2];

        private readonly int[] blurViewLocation = new int[2];

        private readonly PreDrawListener _preDrawListener;

        private readonly GlobalLayoutListener _globalLayoutListener;

        private bool blurEnabled = true;

        private bool initialized;

        private Drawable frameClearDrawable;

        private bool hasFixedTransformationMatrix;

        private Paint paint = new Paint(PaintFlags.FilterBitmap);

        /**
     * @param blurView View which will draw it's blurred underlying content
     * @param rootView Root View where blurView's underlying content starts drawing.
     *                 Can be Activity's root content layout (android.R.id.content)
     *                 or some of your custom root layouts.
     */
        public BlockingBlurController(Context context, View blurView, ViewGroup rootView, int overlayColor = TRANSPARENT)
        {
            this.rootView = rootView;
            this.blurView = blurView;
            this.overlayColor = overlayColor;
            this.blurAlgorithm = new RenderScriptBlur(context);

            int measuredWidth = blurView.MeasuredWidth;
            int measuredHeight = blurView.MeasuredHeight;

            _preDrawListener = new PreDrawListener(this);
            _globalLayoutListener = new GlobalLayoutListener(this);

            if (IsZeroSized(measuredWidth, measuredHeight))
            {
                DeferBitmapCreation();
                return;
            }

            Init(measuredWidth, measuredHeight);
        }

        private int DownScaleSize(float value)
        {
            return (int)System.Math.Ceiling(value / scaleFactor);
        }

        /**
     * Rounds a value to the nearest divisible by {@link #ROUNDING_VALUE} to meet stride requirement
     */
        private int RoundSize(int value)
        {
            if (value % ROUNDING_VALUE == 0)
            {
                return value;
            }

            return value - (value % ROUNDING_VALUE) + ROUNDING_VALUE;
        }

        private void Init(int measuredWidth, int measuredHeight)
        {
            if (IsZeroSized(measuredWidth, measuredHeight))
            {
                blurView.SetWillNotDraw(true);
                return;
            }

            InternalLogger.Info($"Init( width: {measuredWidth}, height: {measuredHeight} )");

            blurView.SetWillNotDraw(false);
            AllocateBitmap(measuredWidth, measuredHeight);
            internalCanvas = new Canvas(internalBitmap);
            initialized = true;

            if (hasFixedTransformationMatrix)
            {
                SetupInternalCanvasMatrix();
            }
        }

        private bool IsZeroSized(int measuredWidth, int measuredHeight)
        {
            return DownScaleSize(measuredHeight) == 0 || DownScaleSize(measuredWidth) == 0;
        }

        private void UpdateBlur()
        {
            if (!blurEnabled || !initialized)
            {
                return;
            }

            InternalLogger.Info("UpdateBlur()");

            if (frameClearDrawable == null)
            {
                internalBitmap.EraseColor(Color.Transparent);
            }
            else
            {
                frameClearDrawable.Draw(internalCanvas);
            }

            if (hasFixedTransformationMatrix)
            {
                rootView.Draw(internalCanvas);
            }
            else
            {
                internalCanvas.Save();
                SetupInternalCanvasMatrix();
                try
                {
                    rootView.Draw(internalCanvas);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }

                internalCanvas.Restore();
            }

            BlurAndSave();
        }

        /**
     * Deferring initialization until view is laid out
     */
        private void DeferBitmapCreation()
        {
            InternalLogger.Info("DeferBitmapCreation()");

            blurView.ViewTreeObserver.AddOnGlobalLayoutListener(_globalLayoutListener);
        }

        private void AllocateBitmap(int measuredWidth, int measuredHeight)
        {
            InternalLogger.Info($"AllocateBitmap( width: {measuredWidth}, height: {measuredHeight} )");

            int nonRoundedScaledWidth = DownScaleSize(measuredWidth);
            int nonRoundedScaledHeight = DownScaleSize(measuredHeight);

            int scaledWidth = RoundSize(nonRoundedScaledWidth);
            int scaledHeight = RoundSize(nonRoundedScaledHeight);

            roundingHeightScaleFactor = (float)nonRoundedScaledHeight / scaledHeight;
            roundingWidthScaleFactor = (float)nonRoundedScaledWidth / scaledWidth;

            internalBitmap = Bitmap.CreateBitmap(scaledWidth, scaledHeight, blurAlgorithm.GetSupportedBitmapConfig());
        }

        /**
     * Set up matrix to draw starting from blurView's position
     */
        private void SetupInternalCanvasMatrix()
        {
            rootView.GetLocationOnScreen(rootLocation);
            blurView.GetLocationOnScreen(blurViewLocation);

            int left = blurViewLocation[0] - rootLocation[0];
            int top = blurViewLocation[1] - rootLocation[1];

            float scaleFactorX = scaleFactor * roundingWidthScaleFactor;
            float scaleFactorY = scaleFactor * roundingHeightScaleFactor;

            float scaledLeftPosition = -left / scaleFactorX;
            float scaledTopPosition = -top / scaleFactorY;

            internalCanvas.Translate(scaledLeftPosition, scaledTopPosition);
            internalCanvas.Scale(1 / scaleFactorX, 1 / scaleFactorY);
        }

        public bool Draw(Canvas canvas)
        {
            if (!blurEnabled || !initialized)
            {
                return true;
            }

            // Not blurring own children
            if (canvas == internalCanvas)
            {
                return false;
            }

            InternalLogger.Info("Draw()");

            UpdateBlur();

            canvas.Save();
            canvas.Scale(scaleFactor * roundingWidthScaleFactor, scaleFactor * roundingHeightScaleFactor);
            canvas.DrawBitmap(internalBitmap, 0, 0, paint);
            canvas.Restore();

            if (overlayColor != TRANSPARENT)
            {
                canvas.DrawColor(new Color(overlayColor));
            }

            return true;
        }

        private void BlurAndSave()
        {
            InternalLogger.Info("BlurAndSave()");

            internalBitmap = blurAlgorithm.Blur(internalBitmap, blurRadius);
            if (!blurAlgorithm.CanModifyBitmap())
            {
                internalCanvas.SetBitmap(internalBitmap);
            }
        }

        public void UpdateBlurViewSize()
        {
            InternalLogger.Info("UpdateBlurViewSize()");

            int measuredWidth = blurView.MeasuredWidth;
            int measuredHeight = blurView.MeasuredHeight;

            Init(measuredWidth, measuredHeight);
        }

        public void Destroy()
        {
            InternalLogger.Info($"Destroy)");
            SetBlurAutoUpdate(false);
            blurAlgorithm.Destroy();
            initialized = false;
        }

        public IBlurViewFacade SetBlurEnabled(bool enabled)
        {
            this.blurEnabled = enabled;
            SetBlurAutoUpdate(enabled);
            blurView.Invalidate();
            return this;
        }

        public IBlurViewFacade SetBlurRadius(float radius)
        {
            this.blurRadius = radius;
            return this;
        }

        public IBlurViewFacade SetBlurAutoUpdate(bool enabled)
        {
            InternalLogger.Info($"SetBlurAutoUpdate( {enabled} )");
            blurView.ViewTreeObserver.RemoveOnPreDrawListener(_preDrawListener);
            if (enabled)
            {
                blurView.ViewTreeObserver.AddOnPreDrawListener(_preDrawListener);
            }

            return this;
        }

        public IBlurViewFacade SetHasFixedTransformationMatrix(bool hasFixedTransformationMatrix)
        {
            this.hasFixedTransformationMatrix = hasFixedTransformationMatrix;
            return this;
        }

        public IBlurViewFacade SetOverlayColor(int overlayColor)
        {
            if (this.overlayColor != overlayColor)
            {
                this.overlayColor = overlayColor;
                blurView.Invalidate();
            }

            return this;
        }

        private class PreDrawListener : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
        {
            private readonly WeakReference<BlockingBlurController> _weakController;

            public PreDrawListener(BlockingBlurController controller)
            {
                _weakController = new WeakReference<BlockingBlurController>(controller);
            }

            public PreDrawListener(IntPtr handle, JniHandleOwnership transfer)
                : base(handle, transfer)
            {
            }

            public bool OnPreDraw()
            {
                if (!_weakController.TryGetTarget(out var controller))
                {
                    return false;
                }

                // Not invalidating a View here, just updating the Bitmap.
                // This relies on the HW accelerated bitmap drawing behavior in Android
                // If the bitmap was drawn on HW accelerated canvas, it holds a reference to it and on next
                // drawing pass the updated content of the bitmap will be rendered on the screen
                controller.UpdateBlur();
                return true;
            }
        }

        private class GlobalLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
        {
            private readonly WeakReference<BlockingBlurController> _weakController;

            public GlobalLayoutListener(BlockingBlurController controller)
            {
                _weakController = new WeakReference<BlockingBlurController>(controller);
            }

            public GlobalLayoutListener(IntPtr handle, JniHandleOwnership transfer)
                : base(handle, transfer)
            {
            }

            public void OnGlobalLayout()
            {
                if (!_weakController.TryGetTarget(out var controller))
                {
                    return;
                }

                controller.blurView.ViewTreeObserver.RemoveOnGlobalLayoutListener(this);

                int measuredWidth = controller.blurView.MeasuredWidth;
                int measuredHeight = controller.blurView.MeasuredHeight;

                controller.Init(measuredWidth, measuredHeight);
            }
        }
    }
}
