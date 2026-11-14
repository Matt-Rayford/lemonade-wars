using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Throws a real rigidbody die across an invisible table in its own physics scene
    /// and records the flight, ahead of time, in a few milliseconds. Walls stand along the
    /// screen's edges and close behind the die once it is inside them, so it bounces off them
    /// and settles in view. Which number ends up on top is the renderer's problem (it relabels the
    /// faces); hops for modifier flips retry until the wanted face lands up.
    /// </summary>
    public sealed class DieThrowSimulator
    {
        public const float StepSeconds = 1f / 60f;
        /// <summary>
        /// Die edge = 1 unit, so real gravity would be ~600. Gentle on purpose: watchable, and at 150
        /// PhysX read every floor contact as a bounce (its threshold is 2 units/s), so the die never rested.
        /// </summary>
        private const float Gravity = 30f;
        private const float MaxThrowSeconds = 5f;
        private const float MaxHopSeconds = 2.5f;
        private const int ThrowAttempts = 6;
        private const int HopAttempts = 40;
        private const float WallHeight = 10f;
        private const float WallThickness = 2f;
        /// <summary>The die's half-diagonal is 0.87: this far inside, no wall can overlap it when it closes.</summary>
        private const float EntryMargin = 1.2f;
        /// <summary>A hop only counts when the die's centre rests at least this far inside the walls.</summary>
        private const float RestMargin = 0.4f;
        private const float SpawnOffset = 2.5f;
        private const float RestSpeed = 0.1f;
        private const int RestSteps = 12;

        private readonly PhysicsScene _physics;
        private readonly Scene _scene;
        private readonly Rigidbody _die;
        private readonly PhysicsMaterial _surface;
        private readonly List<BoxCollider> _walls = new List<BoxCollider>();
        private Vector2[] _arena = new Vector2[0];

        public DieThrowSimulator()
        {
            _scene = SceneManager.CreateScene("DiePhysics",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            _surface = new PhysicsMaterial("DieTable")
            {
                bounciness = 0.35f,
                dynamicFriction = 0.5f,
                staticFriction = 0.6f,
                bounceCombine = PhysicsMaterialCombine.Average,
                frictionCombine = PhysicsMaterialCombine.Average,
            };

            var floor = new GameObject("DieFloor", typeof(BoxCollider));
            var floorBox = floor.GetComponent<BoxCollider>();
            floorBox.size = new Vector3(400f, 2f, 400f);
            floorBox.center = new Vector3(0f, -1f, 0f);
            floorBox.sharedMaterial = _surface;
            SceneManager.MoveGameObjectToScene(floor, _scene);

            var dieGo = new GameObject("PhysicsDie", typeof(BoxCollider), typeof(Rigidbody));
            dieGo.GetComponent<BoxCollider>().sharedMaterial = _surface;
            _die = dieGo.GetComponent<Rigidbody>();
            _die.mass = 1f;
            _die.useGravity = false;
            _die.linearDamping = 0.1f;
            _die.angularDamping = 0.4f;
            _die.maxAngularVelocity = 60f;
            _die.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _die.interpolation = RigidbodyInterpolation.None;
            SceneManager.MoveGameObjectToScene(dieGo, _scene);
        }

        /// <summary>The landing area: a convex quad in xz, a wall along each side. Throws start beyond it.</summary>
        public void SetArena(Vector2[] cornersXz)
        {
            foreach (var wall in _walls)
            {
                Object.Destroy(wall.gameObject);
            }
            _walls.Clear();
            _arena = cornersXz;
            for (int i = 0; i < cornersXz.Length; i++)
            {
                _walls.Add(CreateWall(i, cornersXz[i], cornersXz[(i + 1) % cornersXz.Length]));
            }
        }

        public DieThrowRecording RecordThrow()
        {
            // By design, where the die stops is never grounds for another throw. The only retry is
            // for a die with no readable number, and it is simulated before anything is shown.
            DieThrowRecording last = null;
            for (int attempt = 0; attempt < ThrowAttempts; attempt++)
            {
                last = SimulateThrow();
                if (HasSettledFlat(last))
                {
                    return last;
                }
            }
            return last;
        }

        /// <summary>
        /// A short hop from where the die lies, retried until <paramref name="accepts"/>
        /// likes the resting rotation (the caller checks which number faces up).
        /// </summary>
        public DieThrowRecording RecordHop(Vector3 from, Quaternion fromRotation,
            System.Func<Quaternion, bool> accepts)
        {
            DieThrowRecording last = null;
            for (int attempt = 0; attempt < HopAttempts; attempt++)
            {
                last = SimulateHop(from, fromRotation);
                if (IsSettledInView(last) && accepts(last.FinalRotation))
                {
                    return last;
                }
            }
            return last;
        }

        private BoxCollider CreateWall(int index, Vector2 start, Vector2 end)
        {
            var along = (end - start).normalized;
            var inward = InwardNormal(start, end);
            var middle = (start + end) * 0.5f - inward * (WallThickness * 0.5f);
            var wallGo = new GameObject("DieWall" + index, typeof(BoxCollider));
            wallGo.transform.position = new Vector3(middle.x, WallHeight * 0.5f, middle.y);
            wallGo.transform.rotation = Quaternion.FromToRotation(Vector3.right, new Vector3(along.x, 0f, along.y));
            var box = wallGo.GetComponent<BoxCollider>();
            box.size = new Vector3((end - start).magnitude + 2f * WallThickness, WallHeight, WallThickness);
            box.sharedMaterial = _surface;
            SceneManager.MoveGameObjectToScene(wallGo, _scene);
            return box;
        }

        private DieThrowRecording SimulateThrow()
        {
            var velocity = AimThrow(out var start);
            Launch(start, Random.rotationUniform, velocity, Random.onUnitSphere * Random.Range(10f, 22f));
            SetWalls(false);

            var recording = new DieThrowRecording(StepSeconds);
            bool wallsClosed = false;
            int restSteps = 0;
            int maxSteps = Mathf.RoundToInt(MaxThrowSeconds / StepSeconds);
            for (int step = 0; step < maxSteps; step++)
            {
                Step(recording);
                var position = _die.position;
                if (!wallsClosed && IsInside(position, EntryMargin))
                {
                    SetWalls(true);
                    wallsClosed = true;
                }
                if (wallsClosed && IsResting(ref restSteps))
                {
                    break;
                }
            }
            return recording;
        }

        /// <summary>From off screen, anywhere round its edge, at a point well inside the walls.</summary>
        private Vector3 AimThrow(out Vector3 start)
        {
            var spawn = RandomSpawnOffScreen();
            var direction = (RandomTargetInside() - spawn).normalized;
            float speed = Random.Range(15f, 20f);
            start = new Vector3(spawn.x, Random.Range(2f, 4f), spawn.y);
            return new Vector3(direction.x * speed, Random.Range(-1f, 3f), direction.y * speed);
        }

        private DieThrowRecording SimulateHop(Vector3 from, Quaternion fromRotation)
        {
            var sideways = Random.insideUnitCircle * 1.5f;
            var velocity = new Vector3(sideways.x, Random.Range(4.5f, 6.5f), sideways.y);
            Launch(from, fromRotation, velocity, Random.onUnitSphere * Random.Range(5f, 10f));
            SetWalls(true);

            var recording = new DieThrowRecording(StepSeconds);
            int restSteps = 0;
            int maxSteps = Mathf.RoundToInt(MaxHopSeconds / StepSeconds);
            for (int step = 0; step < maxSteps; step++)
            {
                Step(recording);
                if (IsResting(ref restSteps))
                {
                    break;
                }
            }
            return recording;
        }

        private void Launch(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            _die.transform.SetPositionAndRotation(position, rotation);
            _die.position = position;
            _die.rotation = rotation;
            _die.linearVelocity = velocity;
            _die.angularVelocity = angularVelocity;
            _die.WakeUp();
        }

        private void Step(DieThrowRecording recording)
        {
            _die.AddForce(Vector3.down * Gravity, ForceMode.Acceleration);
            _physics.Simulate(StepSeconds);
            recording.Add(_die.position, _die.rotation);
        }

        private bool IsResting(ref int restSteps)
        {
            bool isStill = _die.linearVelocity.magnitude < RestSpeed && _die.angularVelocity.magnitude < RestSpeed;
            restSteps = isStill ? restSteps + 1 : 0;
            return restSteps >= RestSteps || _die.IsSleeping();
        }

        private static bool HasSettledFlat(DieThrowRecording recording) =>
            recording.DurationSeconds < MaxThrowSeconds - StepSeconds &&
            DieMesh.IsFlat(recording.FinalRotation);

        private bool IsSettledInView(DieThrowRecording recording) =>
            recording.DurationSeconds < MaxThrowSeconds - StepSeconds &&
            IsInside(recording.FinalPosition, RestMargin) &&
            DieMesh.IsFlat(recording.FinalRotation);

        private void SetWalls(bool enabled)
        {
            foreach (var wall in _walls)
            {
                wall.enabled = enabled;
            }
        }

        private bool IsInside(Vector3 position, float margin)
        {
            var point = new Vector2(position.x, position.z);
            for (int i = 0; i < _arena.Length; i++)
            {
                var a = _arena[i];
                var b = _arena[(i + 1) % _arena.Length];
                if (Vector2.Dot(point - a, InwardNormal(a, b)) < margin)
                {
                    return false;
                }
            }
            return _arena.Length > 0;
        }

        private Vector2 InwardNormal(Vector2 a, Vector2 b)
        {
            var along = (b - a).normalized;
            var normal = new Vector2(-along.y, along.x);
            var centroid = Vector2.zero;
            foreach (var corner in _arena)
            {
                centroid += corner / _arena.Length;
            }
            return Vector2.Dot(centroid - a, normal) >= 0f ? normal : -normal;
        }

        /// <summary>A point beyond the walls, off a random stretch of their whole perimeter.</summary>
        private Vector2 RandomSpawnOffScreen()
        {
            float perimeter = 0f;
            for (int i = 0; i < _arena.Length; i++)
            {
                perimeter += (_arena[(i + 1) % _arena.Length] - _arena[i]).magnitude;
            }
            float along = Random.value * perimeter;
            for (int i = 0; i < _arena.Length; i++)
            {
                var a = _arena[i];
                var b = _arena[(i + 1) % _arena.Length];
                float length = (b - a).magnitude;
                if (along <= length || i == _arena.Length - 1)
                {
                    return Vector2.Lerp(a, b, Random.Range(0.1f, 0.9f)) - InwardNormal(a, b) * SpawnOffset;
                }
                along -= length;
            }
            return _arena[0];
        }

        /// <summary>A point in the landing area's middle half: something to throw the die at.</summary>
        private Vector2 RandomTargetInside()
        {
            float u = Random.Range(0.25f, 0.75f);
            float v = Random.Range(0.25f, 0.75f);
            return Vector2.Lerp(
                Vector2.Lerp(_arena[0], _arena[1], u),
                Vector2.Lerp(_arena[3], _arena[2], u), v);
        }
    }
}
