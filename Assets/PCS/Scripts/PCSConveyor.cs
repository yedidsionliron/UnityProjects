using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PCS
{
	[RequireComponent(typeof(Rigidbody))]
	public class PCSConveyor : MonoBehaviour
	{
		Rigidbody rb;
		public float speed;

		[Tooltip("Friction coefficient for force-based belt driving (μ). " +
				 "Overrides PhysX material friction so slanted belts work with zero-friction packages. " +
				 "Typical rubber-on-cardboard ≈ 0.5–0.8.")]
		public float frictionCoefficient = 0.7f;

		private BoxCollider bc;

		private void Start()
		{
			rb = GetComponent<Rigidbody>();
			bc = GetComponent<BoxCollider>();

			// Zero PhysX friction — packages are driven by explicit AddForce, not surface friction.
			// Non-zero friction here would double-drive packages (kinematic surface + explicit force)
			// and cause sticking to belt edges.
			if (bc != null)
				bc.material = new PhysicsMaterial("PCSConveyorSurface")
				{
					dynamicFriction = 0f,
					staticFriction  = 0f,
					frictionCombine = PhysicsMaterialCombine.Minimum,
					bounciness      = 0f,
					bounceCombine   = PhysicsMaterialCombine.Minimum,
				};
		}

		void FixedUpdate()
		{
			// Kinematic belt surface movement (preserves UV scrolling / visual motion)
			transform.position -= transform.forward * speed * Time.fixedDeltaTime;
			Physics.SyncTransforms();
			rb.MovePosition(transform.position + transform.forward * speed * Time.fixedDeltaTime);

			// Force-based driving: explicitly push packages at belt speed regardless of
			// PhysX material friction (essential for slanted belts or zero-friction packages).
			if (bc != null)
				DrivePackages();
		}

		private void DrivePackages()
		{
			// Scan slightly above the belt surface to catch boxes resting on top.
			Vector3 worldCenter = transform.TransformPoint(bc.center + Vector3.up * 0.05f);
			Vector3 halfExtents = new Vector3(
				bc.size.x * 0.5f * transform.lossyScale.x,
				bc.size.y * 0.5f * transform.lossyScale.y + 0.05f,
				bc.size.z * 0.5f * transform.lossyScale.z
			);

			Collider[] hits = Physics.OverlapBox(
				worldCenter, halfExtents, transform.rotation,
				~0, QueryTriggerInteraction.Ignore
			);

			Vector3 beltDir = transform.forward;

			foreach (Collider hit in hits)
			{
				if (hit.gameObject == gameObject) continue;

				Rigidbody packageRb = hit.attachedRigidbody;
				if (packageRb == null || packageRb.isKinematic) continue;

				float slip = speed - Vector3.Dot(packageRb.linearVelocity, beltDir);
				if (Mathf.Abs(slip) < 0.001f) continue;

				// Normal force: project gravity onto belt's surface normal (correct for slanted belts).
				float normalForce   = packageRb.mass * Mathf.Abs(Vector3.Dot(Physics.gravity, transform.up));
				float frictionForce = frictionCoefficient * normalForce;

				// Cap so the package doesn't overshoot belt speed in one physics step.
				float maxForce    = packageRb.mass * Mathf.Abs(slip) / Time.fixedDeltaTime;
				float appliedForce = Mathf.Min(frictionForce, maxForce);

				packageRb.AddForce(beltDir * Mathf.Sign(slip) * appliedForce, ForceMode.Force);
			}
		}
	}
}
