"""Open the isolated saved reference chop, leaving recovered tabs intact."""
path = "D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur/Survivor-TPS-03-baseline-chop.casc"
ok = app.get_data_source_manager().load_scene(path)
print("Load reference chop:", ok)
if not ok:
    raise RuntimeError("Saved reference chop failed to load")
