#region MigraDoc - Creating Documents on the Fly

//
// Authors:
//   Klaus Potzesny
//
// Copyright (c) 2001-2019 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharpCore.com
// http://www.migradoc.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

#endregion

using System;
using System.IO;
using System.Diagnostics;
using PdfSharpCore.Drawing;
using MigraDocCore.DocumentObjectModel.Shapes;
using MigraDocCore.Rendering.MigraDoc.Rendering.Resources;
using PdfSharpCore.Fonts;

namespace MigraDocCore.Rendering
{
    /// <summary>
    /// Renders images.
    /// </summary>
    internal class ImageRenderer : ShapeRenderer
    {
        internal ImageRenderer(XGraphics gfx, Image image, FieldInfos fieldInfos)
            : base(gfx, image, fieldInfos)
        {
            _image = image;
            ImageRenderInfo renderInfo = new ImageRenderInfo();
            renderInfo.DocumentObject = _shape;
            _renderInfo = renderInfo;
        }

        internal ImageRenderer(XGraphics gfx, RenderInfo renderInfo, FieldInfos fieldInfos)
            : base(gfx, renderInfo, fieldInfos)
        {
            _image = (Image)renderInfo.DocumentObject;
        }

        internal override void Format(Area area, FormatInfo previousFormatInfo)
        {
            ImageFormatInfo formatInfo = (ImageFormatInfo)_renderInfo.FormatInfo;
            formatInfo.ImageSource = _image.Source;
            formatInfo.Failure = _failure;
            CalculateImageDimensions();
            base.Format(area, previousFormatInfo);
        }

        protected override XUnit ShapeHeight
        {
            get
            {
                ImageFormatInfo formatInfo = (ImageFormatInfo)_renderInfo.FormatInfo;
                return formatInfo.Height + _lineFormatRenderer.GetWidth();
            }
        }

        protected override XUnit ShapeWidth
        {
            get
            {
                ImageFormatInfo formatInfo = (ImageFormatInfo)_renderInfo.FormatInfo;
                return formatInfo.Width + _lineFormatRenderer.GetWidth();
            }
        }

        internal override void Render()
        {
            RenderFilling();

            ImageFormatInfo formatInfo = (ImageFormatInfo)_renderInfo.FormatInfo;
            Area contentArea = _renderInfo.LayoutInfo.ContentArea;
            XRect destRect = new XRect(contentArea.X, contentArea.Y, formatInfo.Width, formatInfo.Height);

            if (formatInfo.Failure == ImageFailure.None)
            {
                try
                {
                    XRect srcRect = new XRect(formatInfo.CropX, formatInfo.CropY, formatInfo.CropWidth, formatInfo.CropHeight);
                    using (var xImage = XImage.FromImageSource(formatInfo.ImageSource))
                        _gfx.DrawImage(xImage, destRect, srcRect, XGraphicsUnit.Point); //Pixel.
                }
                catch (Exception)
                {
                    RenderFailureImage(destRect);
                }
            }
            else
                RenderFailureImage(destRect);

            RenderLine();
        }

        void RenderFailureImage(XRect destRect)
        {
            _gfx.DrawRectangle(XBrushes.LightGray, destRect);
            string failureString;
            ImageFormatInfo formatInfo = (ImageFormatInfo)RenderInfo.FormatInfo;

            switch (formatInfo.Failure)
            {
                case ImageFailure.EmptySize:
                    failureString = AppResources.DisplayEmptyImageSize;
                    break;

                case ImageFailure.FileNotFound:
                    failureString = AppResources.DisplayImageFileNotFound;
                    break;

                case ImageFailure.InvalidType:
                    failureString = AppResources.DisplayInvalidImageType;
                    break;

                case ImageFailure.NotRead:
                default:
                    failureString = AppResources.DisplayImageNotRead;
                    break;
            }

            // Create stub font
            XFont font = new XFont(GlobalFontSettings.FontResolver.DefaultFontName, 8);
            _gfx.DrawString(failureString, font, XBrushes.Red, destRect, XStringFormats.Center);
        }

        private void CalculateImageDimensions()
        {
            ImageFormatInfo formatInfo = (ImageFormatInfo)_renderInfo.FormatInfo;

            if (formatInfo.Failure == ImageFailure.None)
            {
                XImage xImage = null;
                try
                {
                    xImage = XImage.FromImageSource(formatInfo.ImageSource);
                }
                catch (InvalidOperationException ex)
                {
                    Debug.WriteLine(string.Format(AppResources.InvalidImageType, ex.Message));
                    formatInfo.Failure = ImageFailure.InvalidType;
                }

                try
                {
                    XUnit usrWidth = _image.Width.Point;
                    XUnit usrHeight = _image.Height.Point;
                    bool usrWidthSet = !_image.Width.IsNull;
                    bool usrHeightSet = !_image.Height.IsNull;

                    XUnit resultWidth = usrWidth;
                    XUnit resultHeight = usrHeight;

                    Debug.Assert(xImage != null);
                    double xPixels = xImage.PixelWidth;
                    bool usrResolutionSet = _image.Resolution.HasValue;

                    double horzRes = usrResolutionSet ? _image.Resolution.Value : xImage.HorizontalResolution;
                    double vertRes = usrResolutionSet ? _image.Resolution.Value : xImage.VerticalResolution;

                    if (horzRes == 0 && vertRes == 0)
                    {
                        horzRes = 72;
                        vertRes = 72;
                    }
                    else if (horzRes == 0)
                    {
                        Debug.Assert(false, "How can this be?");
                        horzRes = 72;
                    }
                    else if (vertRes == 0)
                    {
                        Debug.Assert(false, "How can this be?");
                        vertRes = 72;
                    }
                    // ReSharper restore CompareOfFloatsByEqualityOperator

                    XUnit inherentWidth = XUnit.FromInch(xPixels / horzRes);
                    double yPixels = xImage.PixelHeight;
                    XUnit inherentHeight = XUnit.FromInch(yPixels / vertRes);

                    bool lockRatio = !_image.LockAspectRatio.HasValue || _image.LockAspectRatio.Value;

                    double? scaleHeight = _image.ScaleHeight;
                    double? scaleWidth = _image.ScaleWidth;

                    if (lockRatio && !(scaleHeight.HasValue && scaleWidth.HasValue))
                    {
                        if (usrWidthSet && !usrHeightSet)
                        {
                            resultHeight = inherentHeight / inherentWidth * usrWidth;
                        }
                        else if (usrHeightSet && !usrWidthSet)
                        {
                            resultWidth = inherentWidth / inherentHeight * usrHeight;
                        }
// ReSharper disable once ConditionIsAlwaysTrueOrFalse
                        else if (!usrHeightSet && !usrWidthSet)
                        {
                            resultHeight = inherentHeight;
                            resultWidth = inherentWidth;
                        }

                        if (scaleHeight.HasValue)
                        {
                            resultHeight *= scaleHeight.Value;
                            resultWidth *= scaleHeight.Value;
                        }

                        if (scaleWidth.HasValue)
                        {
                            resultHeight *= scaleWidth.Value;
                            resultWidth *= scaleWidth.Value;
                        }
                    }
                    else
                    {
                        if (!usrHeightSet)
                            resultHeight = inherentHeight;

                        if (!usrWidthSet)
                            resultWidth = inherentWidth;

                        if (scaleHeight.HasValue)
                            resultHeight *= scaleHeight.Value;
                        if (scaleWidth.HasValue)
                            resultWidth *= scaleWidth.Value;
                    }

                    formatInfo.CropWidth = (int)xPixels;
                    formatInfo.CropHeight = (int)yPixels;
                    if (_image.PictureFormat != null && !_image.PictureFormat.IsNull())
                    {
                        PictureFormat picFormat = _image.PictureFormat;
                        // Cropping in pixels.
                        XUnit cropLeft = picFormat.CropLeft.Point;
                        XUnit cropRight = picFormat.CropRight.Point;
                        XUnit cropTop = picFormat.CropTop.Point;
                        XUnit cropBottom = picFormat.CropBottom.Point;
                        formatInfo.CropX = (int)(horzRes * cropLeft.Inch);
                        formatInfo.CropY = (int)(vertRes * cropTop.Inch);
                        formatInfo.CropWidth -= (int)(horzRes * ((XUnit)(cropLeft + cropRight)).Inch);
                        formatInfo.CropHeight -= (int)(vertRes * ((XUnit)(cropTop + cropBottom)).Inch);

                        //Scaled cropping of the height and width.
                        double xScale = resultWidth / inherentWidth;
                        double yScale = resultHeight / inherentHeight;

                        cropLeft = xScale * cropLeft;
                        cropRight = xScale * cropRight;
                        cropTop = yScale * cropTop;
                        cropBottom = yScale * cropBottom;

                        resultHeight = resultHeight - cropTop - cropBottom;
                        resultWidth = resultWidth - cropLeft - cropRight;
                    }

                    if (resultHeight <= 0 || resultWidth <= 0)
                    {
                        formatInfo.Width = XUnit.FromCentimeter(2.5);
                        formatInfo.Height = XUnit.FromCentimeter(2.5);
                        Debug.WriteLine(AppResources.EmptyImageSize);
                        _failure = ImageFailure.EmptySize;
                    }
                    else
                    {
                        formatInfo.Width = resultWidth;
                        formatInfo.Height = resultHeight;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format(AppResources.ImageNotReadable, _image.Source.ToString(), ex.Message));
                    formatInfo.Failure = ImageFailure.NotRead;
                }
                finally
                {
                    if (xImage != null)
                        xImage.Dispose();
                }
            }

            if (formatInfo.Failure != ImageFailure.None)
            {
                if (!_image.Width.IsNull)
                    formatInfo.Width = _image.Width.Point;
                else
                    formatInfo.Width = XUnit.FromCentimeter(2.5);

                if (!_image.Height.IsNull)
                    formatInfo.Height = _image.Height.Point;
                else
                    formatInfo.Height = XUnit.FromCentimeter(2.5);
            }
        }


        readonly Image _image;
        string _imageFilePath;
        ImageFailure _failure;
    }
}