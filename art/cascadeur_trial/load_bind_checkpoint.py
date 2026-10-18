"""Open the preserved native bind checkpoint as a separate scene tab."""
path = "D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur/Survivor-TPS-00-imported-bind.casc"
result = app.get_data_source_manager().load_scene(path)
print("Load bind checkpoint:", result)
if not result:
    raise RuntimeError("Checkpoint did not load")
