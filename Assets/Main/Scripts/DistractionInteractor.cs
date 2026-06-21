using UnityEngine;

/// <summary>
/// Player-side scanner for droppable "En Object" distractions. Shows a pooled Q
/// prompt over the object nearest screen centre; pressing Q drops it (see
/// <see cref="Distraction.Trigger"/>).
///
/// All the scanning / single-prompt arbitration lives in <see cref="PlayerInteractor{T}"/>.
/// This class only declares the distraction-specific policy. It wins exact angle ties
/// against <see cref="EnemyInteractionPrompt"/>, so a droppable object in the same spot
/// is preferred over the enemy behind it.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class DistractionInteractor : PlayerInteractor<Distraction>
{
    protected override bool WinsAngleTie => true;    // distractions win exact ties

    protected override bool CanInteract(Distraction d) => d.CanBeTriggered;
    protected override void ShowPrompt(Distraction d, bool show) => d.ShowQPrompt(show);
    protected override void Activate(Distraction d) => d.Trigger();
}
