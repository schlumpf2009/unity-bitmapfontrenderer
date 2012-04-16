/*
 * Copyright (c) 2012 GREE, Inc.
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

namespace BitmapFont {

public partial class Header
{
	public short fontSize;
	public short fontAscent;
	public short metricCount;
	public short sheetWidth;
	public short sheetHeight;
}

public partial class Metric
{
	public short u;
	public short v;
	public sbyte bearingX;
	public sbyte bearingY;
	public byte width;
	public byte height;
	public byte advance;
	public byte first;
	public byte second;
	public byte prevNum;
	public byte nextNum;
}

public partial class Data
{
	public Header header;
	public short[] indecies;
	public Metric[] metrics;
	public string textureName;
}

public class Renderer
{
	public enum Align
	{
		LEFT,
		RIGHT,
		CENTER
	}

	protected Data mData;
	protected Mesh mMesh;
	protected Material mMaterial;
	protected MaterialPropertyBlock mProperty;
	protected string mName;
	protected float mSize;
	protected float mWidth;
	protected float mHeight;
	protected float mLineSpacing;
	protected float mLetterSpacing;
	protected float mTabSpacing;
	protected float mLeftMargin;
	protected float mRightMargin;
	protected Align mAlign;
	protected Metric mAsciiMetric;
	protected Metric mNonasciiMetric;
	protected bool mEmpty;

	public Mesh mesh {get {return mMesh;}}
	public Material material {get {return mMaterial;}}

	public Renderer(string fontName,
		float size = 0,
		float width = 0,
		float height = 0,
		Align align = Align.LEFT,
		float lineSpacing = 1.0f,
		float letterSpacing = 0.0f,
		float tabSpacing = 4.0f,
		float leftMargin = 0.0f,
		float rightMargin = 0.0f)
	{
		ResourceCache cache = ResourceCache.SharedInstance();
		mName = fontName;
		mData = cache.LoadData(mName);
		mMaterial = cache.LoadTexture(
			System.IO.Path.GetDirectoryName(mName) + "/" + mData.textureName);
		mAsciiMetric = SearchMetric("M");
		mNonasciiMetric = SearchMetric("\u004d");
		mMesh = new Mesh();
		mProperty = new MaterialPropertyBlock();

		mSize = size;
		mAlign = align;
		mWidth = width;
		mHeight = height;
		mLineSpacing = lineSpacing;
		mLetterSpacing = letterSpacing;
		mTabSpacing = tabSpacing;
		mLeftMargin = leftMargin;
		mRightMargin = rightMargin;
		mEmpty = true;
	}

	~Renderer()
	{
		ResourceCache cache = ResourceCache.SharedInstance();
		cache.UnloadTexture(mData.textureName);
		cache.UnloadData(mName);
	}

	public class compFirst : IComparer<Metric>
	{
		public int Compare(Metric a, Metric b)
		{
			return a.first.CompareTo(b.first);
		}
	}

	public class compSecond : IComparer<Metric>
	{
		public int Compare(Metric a, Metric b)
		{
			return a.second.CompareTo(b.second);
		}
	}

	protected virtual Metric SearchMetric(string c)
	{
		Metric[] metrics = mData.metrics;
		byte[] b = Encoding.Unicode.GetBytes(c);
		byte first = b[1];
		byte second = b[0];
		short index = mData.indecies[first];

		int offset = index + second;
		if (offset < 0) {
			// not found
			return null;
		}
		if (offset >= mData.header.metricCount)
			offset = mData.header.metricCount - 1;

		Metric m = new Metric();
		if (first != metrics[offset].first) {
			if (index < 0)
				index = 0;
			m.first = first;
			offset = Array.BinarySearch(metrics,
				index, offset - index + 1, m, new compFirst());
			if (offset < 0 || first != metrics[offset].first) {
				// not found
				return null;
			}
		}

		if (second != metrics[offset].second) {
			int left = offset - metrics[offset].prevNum;
			int right = offset + metrics[offset].nextNum;
			m.second = second;
			offset = Array.BinarySearch(metrics,
				left, right - left + 1, m, new compSecond());
		}

		if (offset < 0 || metrics[offset].second != second) {
			// not found
			return null;
		}

		return metrics[offset];
	}

	public virtual void SetText(string text, Color color)
	{
		Color[] colors = new Color[text.Length];
		for (int i = 0; i < text.Length; ++i)
			colors[i] = color;
		SetText(text, colors);
	}

	public virtual void SetText(string text, Color[] colors)
	{
		if (text == null || text.Length == 0) {
			mEmpty = true;
			mMesh.Clear();
			return;
		}

		mEmpty = false;
		int chars = text.Length;
		Vector3[] vertices = new Vector3[chars * 4];
		Vector2[] uv = new Vector2[chars * 4];
		int[] triangles = new int[chars * 6];
		Color[] vertexColors = new Color[chars * 4];
		float fontSize = (float)mData.header.fontSize;
		float scale = mSize / fontSize;
		float width = mWidth / scale;
		float x = mLeftMargin;
		float y = -(float)mData.header.fontAscent;
		float sheetWidth = (float)mData.header.sheetWidth;
		float sheetHeight = (float)mData.header.sheetHeight;
		float asciiAdvance = (float)mAsciiMetric.advance;
		float nonAsciiAdvance = (float)mNonasciiMetric.advance;
		int lastAscii = -1;
		float left = width;
		float right = 0;

		for (int i = 0; i < text.Length; ++i) {
			string c = text.Substring(i, 1);

			if (c.CompareTo("\n") == 0) {
				// LINEFEED
				x = mLeftMargin;
				y -= fontSize * mLineSpacing;
				lastAscii = -1;
				continue;
			} else if (c.CompareTo(" ") == 0) {
				// SPACE
				x += asciiAdvance + mLetterSpacing;
				lastAscii = -1;
				continue;
			} else if (c.CompareTo("\t") == 0) {
				// TAB
				x += (asciiAdvance + mLetterSpacing) * mTabSpacing;
				lastAscii = -1;
				continue;
			} else if (c.CompareTo("\u3000") == 0) {
				// JIS X 0208 SPACE
				x += nonAsciiAdvance + fontSize * mLetterSpacing;
				lastAscii = -1;
				continue;
			}

			if ((c.CompareTo("A") >= 0 && c.CompareTo("Z") <= 0) ||
					(c.CompareTo("a") >= 0 && c.CompareTo("z") <= 0)) {
				// ASCII
				if (lastAscii == -1) {
					// Save index for Auto linefeed
					lastAscii = i;
				}
			} else {
				// non-ASCII
				lastAscii = -1;
			}

			Metric metric = SearchMetric(c);
			if (metric == null) {
				// not found
				continue;
			}

			float advance = (float)metric.advance + fontSize * mLetterSpacing;

			float px = x + advance;
			if (width != 0 && px >= width - mRightMargin) {
				// Auto linefeed.
				int index = lastAscii;
				lastAscii = -1;
				x = mLeftMargin;
				y -= fontSize * mLineSpacing;
				if (index != -1 && (
						(c.CompareTo("A") >= 0 && c.CompareTo("Z") <= 0) ||
						(c.CompareTo("a") >= 0 && c.CompareTo("z") <= 0))) {
					// ASCII
					i = index - 1;
					continue;
				}
			}

			float x0 = x + (float)metric.bearingX;
			float x1 = x0 + (float)metric.width;
			float y0 = y + (float)metric.bearingY;
			float y1 = y0 - (float)metric.height;

			if (left > x0)
				left = x0;
			if (right < x1)
				right = x1;

			x += advance;

			x0 *= scale;
			x1 *= scale;
			y0 *= scale;
			y1 *= scale;

			float dw = 1.0f / (2.0f * (float)metric.width);
			float dh = 1.0f / (2.0f * (float)metric.height);
			float u0 = (float)metric.u + dw;
			float u1 = (float)(metric.u + metric.width) - dw;
			float v0 = sheetHeight - ((float)metric.v + dh);
			float v1 = sheetHeight - ((float)(metric.v + metric.height) - dh);
			u0 /= sheetWidth;
			u1 /= sheetWidth;
			v0 /= sheetHeight;
			v1 /= sheetHeight;

			int vertexOffset = i * 4;
			vertices[vertexOffset + 0] = new Vector3(x1, y0, 0);
			vertices[vertexOffset + 1] = new Vector3(x1, y1, 0);
			vertices[vertexOffset + 2] = new Vector3(x0, y0, 0);
			vertices[vertexOffset + 3] = new Vector3(x0, y1, 0);

			uv[vertexOffset + 0] = new Vector2(u1, v0);
			uv[vertexOffset + 1] = new Vector2(u1, v1);
			uv[vertexOffset + 2] = new Vector2(u0, v0);
			uv[vertexOffset + 3] = new Vector2(u0, v1);

			int triangleOffset = i * 6;
			triangles[triangleOffset + 0] = 0 + vertexOffset;
			triangles[triangleOffset + 1] = 1 + vertexOffset;
			triangles[triangleOffset + 2] = 2 + vertexOffset;
			triangles[triangleOffset + 3] = 2 + vertexOffset;
			triangles[triangleOffset + 4] = 1 + vertexOffset;
			triangles[triangleOffset + 5] = 3 + vertexOffset;

			for (int n = 0; n < 4; ++n)
				vertexColors[vertexOffset + n] = colors[i];
		}

		if (mAlign != Align.LEFT) {
			float tw = right - left;
			float offset;
			if (mAlign == Align.CENTER) {
				offset = (mWidth - mLeftMargin - mRightMargin - tw) / 2.0f;
			} else {
				// Align.RIGHT
				offset = mWidth - mRightMargin - tw;
			}
			offset *= scale;

			for (int i = 0; i < vertices.Length; ++i)
				vertices[i].x += offset;
		}

		mMesh.Clear();
		mMesh.vertices = vertices;
		mMesh.uv = uv;
		mMesh.triangles = triangles;
		mMesh.colors = vertexColors;
		mMesh.RecalculateNormals();
		mMesh.RecalculateBounds();
		mMesh.Optimize();
	}

	public virtual void Render(Matrix4x4 matrix, Camera camera = null)
	{
		if (mEmpty)
			return;
		Graphics.DrawMesh(mMesh, matrix, mMaterial, 0, camera);
	}
}

}	// namespace BitmapFont