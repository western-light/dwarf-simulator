# Low Poly World Builder

## Utilisation directe

1. Ouvrir `Tools > Dwarf Simulator > Low Poly World Builder`.
2. Sculpter normalement un `Terrain` Unity puis le selectionner dans la Hierarchy.
3. Cliquer sur **Convertir le Terrain selectionne**.
4. Continuer a modifier la heightmap Unity et cliquer sur **Regenerer le terrain low-poly** dans l'Inspector.
5. Sélectionner le Terrain ou son objet `LP Terrain Mesh`, puis créer une route **Ville**, **Terre**, **Montagne**, **Fissurée** ou **Neige**.
6. Modifier le tracé avec les outils **Splines** de Unity. Pour utiliser une spline existante, ajouter **Low Poly Road** au même objet que son `SplineContainer`.
7. Cliquer sur **Régénérer la route** pour appliquer le tracé et le style. L’Inspector affiche le résultat ou indique ce qui bloque.

Lors de la première génération d’une ancienne route dépourvue de spline Unity, son tracé est transféré vers un `SplineContainer`. Toutes les splines du conteneur possédant au moins deux nœuds sont générées.

Pour déplacer toute la route, déplacer son GameObject. Les nœuds et le mesh se déplacent ensemble. Une nouvelle génération recalcule le raccord au sol.

Quand une portion quitte le terrain, le générateur prolonge la route à la hauteur du raccord ou de la spline. L’Inspector indique les échantillons sans appui.

Avec `Follow Terrain`, la spline guide le tracé horizontal. La chaussée suit les grandes variations du relief et limite son dévers sur la largeur. `Profile Smoothing` règle l’adoucissement des bosses, `Maximum Crossfall` règle le dévers, et `Terrain Sample Spacing` règle la finesse des mesures. Renseigner `Terrain Source` pour imposer un terrain précis, ou laisser le champ vide pour la détection automatique. Désactiver `Follow Terrain` pour utiliser la hauteur de la spline, par exemple pour un pont.

Dans les virages serrés, le bord intérieur se resserre progressivement. Le marquage des routes de ville reste sur l’axe de la spline. Près d’un obstacle latéral, le bord de l’asphalte peut aussi se resserrer pour limiter la bosse.

## Direction artistique

- Le rendu utilise des couleurs de sommets et des normales plates, sans textures photo.
- Palette par defaut : herbe vert olive claire, roche gris desature, neige bleu-blanc froid.
- `Resolution 65` donne de grandes facettes ; `129` donne davantage de detail.
- La taille physique est celle du Terrain source. Elle est affichee et modifiable dans l'Inspector de `LP Terrain Mesh`.
- `Rock Slope 35-45` et `Snow Height 0.68-0.78` conviennent a la plupart des montagnes.
- La variation de couleur doit rester faible (`0.04-0.08`) pour conserver une palette propre.

## Routes de montagne

- Garder `Follow Terrain` et `Generate Embankment` actifs.
- Tracer des lacets sur les flancs raides. L’Inspector signale une pente supérieure ou égale à 45 % après génération.
- `Maximum Fill Depth` limite la hauteur des soutènements avec un appui au sol.
- `Shoulder Width` règle la largeur de l’accotement. `Maximum Embankment Width` limite l’étendue du talus.
- Pour une boucle, activer `Closed` sur la spline Unity.
- Pour un pont ou une portion volontairement surelevee, desactiver `Follow Terrain` puis regler la hauteur des points.

## Peindre des chemins sans modifier le relief

1. Selectionner `LP Terrain Mesh`.
2. Choisir `Paint Layer` : Path, Dirt, Grass, Rock ou Snow.
3. Regler `Brush Radius`, `Brush Opacity` et `Brush Hardness`.
4. Cliquer sur **Peindre dans la Scene**, puis dessiner au clic gauche.
5. Utiliser `Alt + souris` pour continuer a deplacer la camera.

Les coups de pinceau sont conserves et reappliques lors d'une regeneration du terrain.

## Optimisations appliquees

- Aucun recalcul automatique des routes.
- Subdivision adaptee a la longueur des portions pour garder les virages propres sans surcharger les petites sections.
- Le mesh du terrain reutilise par defaut le `TerrainCollider` source au lieu de dupliquer un lourd `MeshCollider`.
- Les assets ne sont plus forces a etre sauvegardes sur disque a chaque deplacement.
- La reception des ombres URP est desactivee par defaut sur le materiau low-poly afin d'eviter les anneaux noirs des cascades d'ombres.

Les meshes et le materiau generes sont conserves dans `Assets/LowPolyWorld/Generated`.
