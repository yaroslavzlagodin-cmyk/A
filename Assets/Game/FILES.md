# Карта файлів Broken Heart

Актуальний запуск, керування, обмеження і список змін описані в README.md.

- Scenes/Menu.unity — стартове меню; Scenes/Game.unity — ігровий світ.
- Scripts/Core/Profiles — HeroData, бібліотека героїв/світів, активна сесія, завантаження й пауза.
- Scripts/UI — меню, тема меню, інвентар/крафт, стан світу.
- Scripts/World — типи блоків, суцільний процедурний рельєф, чанки, Tilemap, взаємодія.
- Scripts/Player — рух, фізика й камера.
- Scripts/Inventory — предмети, слоти, підбирання й випадання.
- Scripts/Crafting — рецепти й крафт.
- Scripts/Combat — здоров'я, атака, вороги.
- Scripts/Systems — пам'ять, збереження, доба, погода, глузд, відпочинок, Ноксель.
- Scripts/Systems/Energy — енергія та пристрої.
- Data/Heroes — п'ять описів рас; Sprites/Hero_*.png — їх спрайти.
- Data, Tiles, Sprites, Materials — налаштовані ігрові ресурси.
- Editor/PrototypeBuilder.cs, FoundationUpgrade.cs — створення базових сцен і етапів.
- Editor/MenuSetup.cs — налаштування меню та героїв.
- Editor/PrototypeChecks.cs, MenuChecks.cs — інтеграційні Play Mode перевірки.

AmbientAudio.cs та його .meta видалено; звук вимкнено. Для нового меню змінено також ProjectSettings/EditorBuildSettings.asset. Unity створює відповідні .meta для нових ресурсів.

Current update adds Editor/VisualUpgrade.cs, Scripts/UI/Localization.cs and Resources/Translations.txt; connects the ten DarkFantasy32 tiles and item icons; creates GrassCover and two recipes. PlayerInventory and Game.unity now use 27 slots / 9 hotbar; SaveSystem migrates 24-slot payloads without dropping stacks. MenuChecks covers both languages, migration and ninth-slot selection.
