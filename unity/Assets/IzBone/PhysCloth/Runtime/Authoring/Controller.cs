﻿using System;
using UnityEngine;

using Unity.Mathematics;
using static Unity.Mathematics.math;


namespace IzBone.PhysCloth.Authoring {
	using Common;
	using Common.Field;
	
	public sealed class ParticleMng {
		public int idx;

		// パーティクルの元となるボーンの根本（Head）と先端（Tail）のTransform。
		// 根本は一つだが、先端は複数ある場合がある。
		// Particleは先端平均位置に配置され、根本のTranformの回転・位置にフィードバックされる。
		public readonly Transform transHead;
		public readonly Transform[] transTail;

		public quaternion defaultHeadRot;		// 初期姿勢
		public float4x4 defaultHeadL2P;			// 初期L2P行列
		public float3 defaultTailLPos;			// 初期先端ローカル位置
		public ParticleMng parent, child, left, right;	// 上下左右のパーティクル。childは一番最初の子供
		public float m;
		public float r;
		public float maxAngle;
		public float angleCompliance;
		public HalfLife restoreHL;		// 初期位置への復元半減期
		public float maxMovableRange;	// デフォルト位置からの移動可能距離

		public ParticleMng(int idx, Transform transHead, Transform[] transTail) {
			this.idx = idx;
			this.transHead = transHead;
			this.transTail = transTail;
			resetDefaultPose();
		}

		// Headのみを指定して生成する
		static public ParticleMng generateByTransHead(int idx, Transform transHead) {
			if (transHead.childCount == 0) throw new ArgumentException("The transHead must have at least one child.");
			var transTail = new Transform[ transHead.childCount ];
			for (int i=0; i<transHead.childCount; ++i) transTail[i] = transHead.GetChild(i);

			return new ParticleMng( idx, transHead, transTail );
		}

		// Tailのみを指定して生成する
		static public ParticleMng generateByTransTail(int idx, Transform transTail) {
			return new ParticleMng( idx, null, new[]{transTail} );
		}

		public void setParams(
			float m,
			float r,
			float maxAngle,
			float angleCompliance,
			HalfLife restoreHL,
			float maxMovableRange
		) {
			this.m = m;
			this.r = r;
			this.angleCompliance = angleCompliance;
			this.maxAngle = maxAngle;
			this.restoreHL = restoreHL;
			this.maxMovableRange = maxMovableRange;
		}

		public void resetDefaultPose() {
			if (transHead==null) {
				defaultHeadRot = default;
				defaultHeadL2P = default;
				defaultTailLPos = 0;
			} else {
				defaultHeadRot = transHead.localRotation;
				defaultHeadL2P = Unity.Mathematics.float4x4.TRS(
					transHead.localPosition,
					transHead.localRotation,
					transHead.localScale
				);

				defaultTailLPos = default;
				foreach (var i in transTail) defaultTailLPos += (float3)i.localPosition;
				defaultTailLPos /= transTail.Length;
			}
		}

		public float3 getTailWPos() {
			if (transHead == null) {
				Unity.Assertions.Assert.IsTrue(transTail.Length == 1);
				return transTail[0].position;
			}
			return transHead.localToWorldMatrix.MultiplyPoint( defaultTailLPos );
		}
	}

	public sealed class ConstraintMng {
		public enum Mode {
			Distance,			// 距離で拘束する
			MaxDistance,		// 指定距離未満になるように拘束する(最大距離を指定)
			Axis,				// 移動可能軸を指定して拘束する
		}
		public Mode mode;
		public int srcPtclIdx, dstPtclIdx;

		public float compliance;
		public float4 param;
	}

}
