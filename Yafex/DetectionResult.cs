using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex
{
	public class DetectionResult
	{
		public int Confidence { get; private set; }
		public object? Context { get; private set; }

		public DetectionResult(int confidence, object? context) {
			this.Confidence = confidence;
			this.Context = context;
		}

		public bool Succeded() => Confidence > 0;
	}
}
