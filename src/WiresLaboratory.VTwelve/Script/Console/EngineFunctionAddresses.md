# Engine function addresses (recovered from `Tribes2.exe`)

Console registrations recovered by disassembling `.text` (1,002,556 instructions),
locating the registrar at `0x00426450` (388 call sites), and reading each call's
arguments back in cdecl order.

Addresses are **image-relative to base `0x00400000`**, from the shipped build
(`Tribes2.exe`, 4,354,048 bytes). They are anchors for navigating the code that has no
string literals at all — the sim loop, the physics integrator, the ghost manager.

## Corroborated (353)

The usage string names the same method as the registration, so name, address and
signature agree independently. These are safe to build against.

| Symbol | Address | Signature |
|---|---|---|
| `setScale` | `0x0058ac00` | `obj.setScale(<xs ys zs>)` |
| `setTransform` | `0x0058ab20` | `obj.setTransform(T)` |
| `AIConnection::clearStep` | `0x00477f70` | `ai.clearStep()` |
| `AIConnection::clearTasks` | `0x00478310` | `ai.clearTasks()` |
| `AIConnection::clientDetected` | `0x00477a20` | `ai.clientDetected(client)` |
| `AIConnection::drop` | `0x00476e50` | `ai.drop()` |
| `AIConnection::listTasks` | `0x004783e0` | `ai.listTasks()` |
| `AIConnection::missionCycleCleanup` | `0x00478480` | `ai.missionCycleCleanup()` |
| `AIConnection::pressFire` | `0x00477de0` | `ai.pressFire([sustain count])` |
| `AIConnection::pressGrenade` | `0x00477e60` | `ai.pressGrenade()` |
| `AIConnection::pressJet` | `0x00477e40` | `ai.pressJet()` |
| `AIConnection::pressJump` | `0x00477e20` | `ai.pressJump()` |
| `AIConnection::pressMine` | `0x00477e80` | `ai.pressMine()` |
| `AIConnection::removeTask` | `0x004783b0` | `ai.removeTask(id)` |
| `AIConnection::setBlinded` | `0x00477aa0` | `ai.setBlinded(durationMS)` |
| `AIConnection::setDangerLocation` | `0x00477ad0` | `ai.setDangerLocation(point3F [, durationTicks])` |
| `AIConnection::setDetectPeriod` | `0x00477a50` | `ai.setDetectPeriod()` |
| `AIConnection::setEngageTarget` | `0x004777a0` | `ai.setEngageTarget(client)` |
| `AIConnection::setPath` | `0x004780d0` | `ai.setPath([toLoc])` |
| `AIConnection::setPilotAim` | `0x004773c0` | `ai.setPilotAim(point3F)` |
| `AIConnection::setPilotDestination` | `0x00477340` | `ai.setPilotDestination(point3F [, maxSpeed])` |
| `AIConnection::setPilotPitchRange` | `0x00477420` | `ai.setPilotPitchRange(pitchUpMax, pitchDownMax, pitchIncMax)` |
| `AIConnection::setSkillLevel` | `0x00477080` | `ai.setSkillLevel(float)` |
| `AIConnection::setTargetObject` | `0x00477b50` | `ai.setTargetObject(object [, range, mode: destroy/repair/laze])` |
| `AIConnection::setTurretMounted` | `0x00477310` | `ai.setTurretMounted(turretId)` |
| `AIConnection::setVictim` | `0x00477840` | `ai.setVictim(client, corpseObject)` |
| `AIConnection::setWeaponInfo` | `0x00477ea0` | `ai.setWeaponInfo(projectile, minDist, maxDist [, triggerCount, requiredEnergy, errorFactor]);` |
| `AIConnection::stepEngage` | `0x00478150` | `ai.stepEngage(client)` |
| `AIConnection::stepEscort` | `0x00477f90` | `ai.stepEscort(client)` |
| `AIConnection::stepIdle` | `0x00478220` | `ai.stepIdle(point3)` |
| `AIConnection::stepJet` | `0x00478020` | `ai.stepJet(toLoc)` |
| `AIConnection::stepMove` | `0x00477100` | `ai.stepMove(point3 [, tolerance, mode])` |
| `AIConnection::stepRangeObject` | `0x00477c60` | `ai.stepRangeObject(object, weapon, minDist, maxDist [, nearLocation])` |
| `AIConnection::stop` | `0x004770e0` | `ai.stop()` |
| `AIObjectiveQ::sortByWeight` | `0x0047db40` | `aiQ.sortByWeight()` |
| `AITask::reMonitor` | `0x0047cfb0` | `ai.reMonitor()` |
| `AITask::reWeight` | `0x0047cf70` | `ai.reWeight()` |
| `AITask::setMonitorFreq` | `0x0047cf80` | `ai.setMonitorFreq(freq)` |
| `AITask::setWeight` | `0x0047cf10` | `ai.setWeight(weight)` |
| `AITask::setWeightFreq` | `0x0047cee0` | `ai.setWeightFreq(freq)` |
| `BanList::add` | `0x00698840` | `BanList::add( id, TA, banTime )` |
| `BanList::addAbsolute` | `0x00698800` | `BanList::addAbsolute( id, TA, banTime )` |
| `BanList::export` | `0x006988e0` | `BanList::export( filename )` |
| `BanList::removeBan` | `0x00698880` | `BanList::removeBan( id, TA )` |
| `BeaconObject::setBeaconType` | `0x006a3b90` | `beaconObject.setBeaconType(type);` |
| `Camera::setFlyMode` | `0x005ccbb0` | `camera.setFlyMode()` |
| `Camera::setOrbitMode` | `0x005ccac0` | `camera.setOrbitMode(obj, Transform, min-dist, max-dist, cur-dist, <ownClientObj>)` |
| `ClientTarget::addPotentialTask` | `0x00671880` | `target.addPotentialTask()` |
| `ClientTarget::createWaypoint` | `0x00671810` | `target.createWaypoint(text)` |
| `ClientTarget::sendToServer` | `0x006717b0` | `target.sendToServer()` |
| `ClientTarget::setText` | `0x006718f0` | `target.setText(text)` |
| `CreatorTree::clear` | `0x00465230` | `creator.clear();` |
| `DbgFileView::clearBreakPositions` | `0x004b8b80` | `fileView.clearBreakPositions()` |
| `DbgFileView::removeBreak` | `0x004b8bd0` | `fileView.removeBreak(line)` |
| `DbgFileView::setBreak` | `0x004b8bb0` | `fileView.setBreak(line)` |
| `DbgFileView::setBreakPosition` | `0x004b8b90` | `fileView.setBreakPosition(line)` |
| `DbgFileView::setCurrentLine` | `0x004b8ae0` | `fileView.setCurrentLine(line, displayLine)` |
| `DebugView::clearLines` | `0x0061afb0` | `debugView.clearLines()` |
| `EditManager::gotoBookmark` | `0x00454d60` | `editor.gotoBookmark(<1-0>);` |
| `EditManager::setBookmark` | `0x00454d00` | `editor.setBookmark(<1-0>);` |
| `EditTSCtrl::renderCircle` | `0x004682d0` | `EditTSCtrl.renderCircle(pos, normal, radius, <segments>` |
| `EditTSCtrl::renderLine` | `0x00468a60` | `EditTSCtrl.renderLine(start, end, <width>` |
| `EditTSCtrl::renderTriangle` | `0x004688f0` | `EditTSCtrl.renderTriangle(pnt, pnt, pnt)` |
| `FileObject::close` | `0x0043dc90` | `file.close()` |
| `FileObject::writeLine` | `0x0043dc70` | `file.writeLine(text)` |
| `FloorPlan::addStaticCenter` | `0x00499910` | `obj.addStaticCenter( shape )` |
| `FloorPlan::addStaticGeom` | `0x00499980` | `obj.addStaticGeom( shape )` |
| `FloorPlan::generate` | `0x004998a0` | `obj.generate()` |
| `FloorPlan::upload` | `0x004998c0` | `obj.upload()` |
| `ForceFieldBare::close` | `0x00674d60` | `obj.close()` |
| `ForceFieldBare::open` | `0x00674d40` | `obj.open()` |
| `GameConnection::activateGhosting` | `0x005fd9b0` | `conn.activateGhosting()` |
| `GameConnection::listenTo` | `0x005fe180` | `conn.listenTo(clientId, true|false)` |
| `GameConnection::listenToAll` | `0x005fe1f0` | `conn.listenToAll()` |
| `GameConnection::listenToNone` | `0x005fe210` | `conn.listenToNone()` |
| `GameConnection::resetGhosting` | `0x005fd9d0` | `conn.resetGhosting()` |
| `GameConnection::scopeCommanderMap` | `0x005fdff0` | `conn.scopeCommanderMap(bool)` |
| `GameConnection::sendTargetTo` | `0x005fddc0` | `conn.sendTargetTo(conn, assign)` |
| `GameConnection::sendTargetToServer` | `0x005fdd10` | `conn.sendTargetToServer(id, pos)` |
| `GameConnection::setBlackOut` | `0x005fe370` | `conn.setBlackOut(fadeTOBlackBool, timeMS)` |
| `GameConnection::setControlCameraFov` | `0x005fe020` | `conn.setControlCameraFov(fov)` |
| `GameConnection::setDisconnectReason` | `0x005fe480` | `conn.setDisconnectReason( reason )` |
| `GameConnection::setMissionCRC` | `0x005fe3b0` | `conn.setMissionCRC(crc)` |
| `GameConnection::setObjectActiveImage` | `0x005fe2c0` | `conn.setObjectActiveImage(obj, imageSlot)` |
| `GameConnection::setReceivedDataBlocks` | `0x005fe450` | `conn.setReceivedDataBlocks(bool)` |
| `GameConnection::setSensorGroup` | `0x005fdc90` | `conn.setSensorGroup(groupId)` |
| `GameConnection::setTargetId` | `0x005fdf10` | `conn.setTargetId(targetId)` |
| `GameConnection::setTargetPos` | `0x005fdf60` | `conn.setTargetPos(Point3F)` |
| `GameConnection::setVehicleTeleportEnabled` | `0x005fe4d0` | `conn.setVehicleTeleportEnabled( bool )` |
| `GameConnection::setVoiceChannels` | `0x005fe230` | `conn.setVoiceChannels(0-3)` |
| `GameConnection::setVoiceDecodingMask` | `0x005fe260` | `conn.setVoiceDecodingMask(mask)` |
| `GameConnection::setVoiceEncodingLevel` | `0x005fe290` | `conn.setVoiceEncodingLevel(codecLevel)` |
| `GameConnection::transmitDataBlocks` | `0x005fd890` | `conn.transmitDataBlocks(seq)` |
| `GuiAviBitmapCtrl::play` | `0x004de4b0` | `obj.play();` |
| `GuiAviBitmapCtrl::stop` | `0x004de4c0` | `obj.stop();` |
| `GuiBitmapCtrl::setBitmap` | `0x004ad770` | `guiBitmapCtrl.setBitmap(blah)` |
| `GuiBitmapCtrl::setValue` | `0x004ad740` | `guiBitmapCtrl.setValue(xAxis, yAxis)` |
| `GuiCanvas::cursorOff` | `0x004aead0` | `canvas.cursorOff()` |
| `GuiCanvas::cursorOn` | `0x004aeab0` | `canvas.cursorOn()` |
| `GuiCanvas::hideCursor` | `0x004aeb80` | `canvas.hideCursor()` |
| `GuiCanvas::popDialog` | `0x004aea00` | `canvas.popDialog(<ctrl>)` |
| `GuiCanvas::pushDialog` | `0x004ae980` | `canvas.pushDialog(ctrl)` |
| `GuiCanvas::renderFront` | `0x004aeb50` | `canvas.renderFront(bool)` |
| `GuiCanvas::repaint` | `0x004aeba0` | `canvas.repaint()` |
| `GuiCanvas::reset` | `0x004aebc0` | `canvas.reset()` |
| `GuiCanvas::setContent` | `0x004ae900` | `canvas.setContent(ctrl)` |
| `GuiCanvas::setCursorPos` | `0x004aec60` | `canvas.setCursorPos(pos)` |
| `GuiCanvas::showCursor` | `0x004aeb70` | `canvas.showCursor()` |
| `GuiCanvas::updateCursorState` | `0x004aece0` | `canvas.updateCursorState()` |
| `GuiChunkedBitmapCtrl::setBitmap` | `0x004b4520` | `ctrl.setBitmap( name );` |
| `GuiCommanderMap::followLastSelected` | `0x0065f3c0` | `commanderMap.followLastSelected();` |
| `GuiCommanderMap::resetCamera` | `0x0065f3d0` | `commanderMap.resetCamera()` |
| `GuiCommanderMap::setMouseMode` | `0x0065f430` | `commanderMap.setMouseMode(mode)` |
| `GuiCommanderMap::setTargetTypeVisible` | `0x0065f360` | `commanderMap.setTargetTypeVisible(type, bool)` |
| `GuiCommanderTree::openCategory` | `0x00663f10` | `commanderTree.openCategory(name, bool)` |
| `GuiCommanderTree::reset` | `0x00664090` | `commanderTree.reset()` |
| `GuiControl::makeFirstResponder` | `0x004b7440` | `ctrl.makeFirstResponder(value)` |
| `GuiControl::resize` | `0x004b74c0` | `ctrl.resize(x,y,w,h)` |
| `GuiControl::setActive` | `0x004b73f0` | `ctrl.setActive(value)` |
| `GuiControl::setExtent` | `0x004b7570` | `ctrl.setExtent(w,h)` |
| `GuiControl::setPosition` | `0x004b7520` | `ctrl.setPosition(x,y)` |
| `GuiControl::setProfile` | `0x004b7480` | `ctrl.setProfile(profileI)` |
| `GuiControl::setValue` | `0x004b73b0` | `ctrl.setValue(value)` |
| `GuiControl::setVisible` | `0x004b7420` | `ctrl.setVisible(value)` |
| `GuiEditCtrl::addNewCtrl` | `0x004ba0b0` | `editCtrl.addNewCtrl(ctrl)` |
| `GuiEditCtrl::bringToFront` | `0x004ba1e0` | `editCtrl.bringToFront()` |
| `GuiEditCtrl::justify` | `0x004ba1c0` | `editCtrl.justify(mode)` |
| `GuiEditCtrl::pushToBack` | `0x004ba1f0` | `editCtrl.pushToBack()` |
| `GuiEditCtrl::select` | `0x004ba0f0` | `editCtrl.select(ctrl)` |
| `GuiEditCtrl::setCurrentAddSet` | `0x004ba130` | `editCtrl.setCurrentAddSet(ctrl)` |
| `GuiEditCtrl::setRoot` | `0x004ba070` | `editCtrl.setRoot(root)` |
| `GuiEmailBrowser::addRow` | `0x0067b8e0` | `browser.addRow( id, from, subject, received, flags )` |
| `GuiEmailBrowser::clear` | `0x0067b8c0` | `browser.clear()` |
| `GuiEmailBrowser::removeRowById` | `0x0067b920` | `browser.removeRowById( id )` |
| `GuiEmailBrowser::removeRowByIndex` | `0x0067b940` | `browser.removeRowByIndex( index )` |
| `GuiEmailBrowser::selectRowById` | `0x0067b9f0` | `browser.selectRowById( id )` |
| `GuiEmailBrowser::setRow` | `0x0067b960` | `browser.setRow( id, from, subject, received, flags )` |
| `GuiEmailBrowser::setRowFlags` | `0x0067b9a0` | `browser.setRowFlags( id, flags )` |
| `GuiEmailBrowser::sort` | `0x0067ba20` | `browser.sort()` |
| `GuiFilterCtrl::identity` | `0x004b5d40` | `guiFilterCtrl.identity()` |
| `GuiFilterCtrl::setValue` | `0x004b5ca0` | `guiFilterCtrl.setValue(f1, f2, ...)` |
| `GuiFrameSetCtrl::addColumn` | `0x004ca710` | `gfsc.addColumn();` |
| `GuiFrameSetCtrl::addRow` | `0x004ca780` | `gfsc.addRow();` |
| `GuiFrameSetCtrl::frameMinExtent` | `0x004ca690` | `gfsc.frameMinExtent(index, w, h)` |
| `GuiFrameSetCtrl::frameMovable` | `0x004ca630` | `gfsc.frameMovable(index, enable)` |
| `GuiFrameSetCtrl::removeColumn` | `0x004ca7f0` | `gfsc.removeColumn();` |
| `GuiFrameSetCtrl::removeRow` | `0x004ca860` | `gfsc.removeRow();` |
| `GuiFrameSetCtrl::setColumnOffset` | `0x004caa10` | `gfsc.setColumnOffset(index, offset);` |
| `GuiFrameSetCtrl::setRowOffset` | `0x004caad0` | `gfsc.setRowOffset(index, offset);` |
| `GuiInspector::apply` | `0x004bdbf0` | `inspector.apply(newName)` |
| `GuiInspector::inspect` | `0x004bdba0` | `inspector.inspect(obj)` |
| `GuiMLTextCtrl::addText` | `0x004d3370` | `[MLTextCtrl].addText("text", reformatBool);` |
| `GuiMLTextCtrl::scrollToTag` | `0x004d33c0` | `[MLTextCtrl].scrollToTag([tag id]);` |
| `GuiMLTextCtrl::scrollToTop` | `0x004d33e0` | `[MLTextCtrl].scrollToTop();` |
| `GuiMLTextCtrl::setText` | `0x004d3330` | `[MLTextCtrl].setText("text");` |
| `GuiMessageVectorCtrl::detach` | `0x004d82a0` | `[GuiMessageVectorCtrl].detach()` |
| `GuiPopUpMenuCtrl::add` | `0x004c8b10` | `menu.add(name,idNum{,scheme})` |
| `GuiPopUpMenuCtrl::addScheme` | `0x004c8b60` | `menu.addScheme(id, fontColor, fontColorHL, fontColorSEL)` |
| `GuiPopUpMenuCtrl::clear` | `0x004c8d60` | `menu.clear()` |
| `GuiPopUpMenuCtrl::forceClose` | `0x004c8de0` | `menu.forceClose()` |
| `GuiPopUpMenuCtrl::forceOnAction` | `0x004c8dc0` | `menu.forceOnAction()` |
| `GuiPopUpMenuCtrl::replaceText` | `0x004c8fa0` | `menu.replaceText(bool)` |
| `GuiPopUpMenuCtrl::setEnumContent` | `0x004c8e50` | `menu.setEnumContent(class, enum)` |
| `GuiPopUpMenuCtrl::setSelected` | `0x004c8e10` | `menu.setSelected(id)` |
| `GuiPopUpMenuCtrl::setText` | `0x004c8d30` | `menu.setText(text)` |
| `GuiPopUpMenuCtrl::setValue` | `0x004c8d30` | `menu.setValue(text)` |
| `GuiScrollCtrl::scrollToBottom` | `0x004be670` | `control.scrollToBottom();` |
| `GuiTerrPreviewCtrl::reset` | `0x00465e80` | `guiTerrPreviewCtrl.reset()` |
| `GuiTerrPreviewCtrl::setOrigin` | `0x00465ee0` | `guiTerrPreviewCtrl.setOrigin(x,y)` |
| `GuiTerrPreviewCtrl::setRoot` | `0x00465e90` | `guiTerrPreviewCtrl.setRoot()` |
| `GuiTextListCtrl::clear` | `0x004c4210` | `textList.clear()` |
| `GuiTextListCtrl::clearSelection` | `0x004c4090` | `textList.clearSelection()` |
| `GuiTextListCtrl::removeRow` | `0x004c4370` | `textList.removeRow(index)` |
| `GuiTextListCtrl::removeRowById` | `0x004c4350` | `textList.removeRowById(id)` |
| `GuiTextListCtrl::scrollVisible` | `0x004c43a0` | `textList.scrollVisible(index)` |
| `GuiTextListCtrl::setRowActive` | `0x004c43f0` | `textlist.setRowActive(id, <bool>)` |
| `GuiTextListCtrl::setSelectedById` | `0x004c4010` | `textList.setSelectedById(id)` |
| `GuiTextListCtrl::setSelectedRow` | `0x004c4060` | `textList.setSelectedRow(index)` |
| `GuiTextListCtrl::sort` | `0x004c4150` | `textList.sort(colId{, increasing})` |
| `GuiTextListCtrl::sortNumerical` | `0x004c41b0` | `textList.sortNumerical(colId{, increasing})` |
| `GuiTreeView::open` | `0x004cf270` | `treeView.open(obj)` |
| `GuiTreeViewCtrl::clear` | `0x004ceef0` | `tree.clear();` |
| `GuiTreeViewCtrl::moveItemUp` | `0x004cf040` | `tree.moveItemUp(item);` |
| `GuiVoteCtrl::setNoValue` | `0x004dc9e0` | `ctrl.setNoValue(value)` |
| `GuiVoteCtrl::setPassValue` | `0x004dc980` | `ctrl.setPassValue(value)` |
| `GuiVoteCtrl::setQuorumValue` | `0x004dc950` | `ctrl.setQuorumValue(value)` |
| `GuiVoteCtrl::setYesValue` | `0x004dc9b0` | `ctrl.setYesValue(value)` |
| `HTTPObject::get` | `0x005bdd50` | `obj.get(addr, request-uri, <query>)` |
| `HTTPObject::post` | `0x005bdd80` | `obj.post(addr, request-uri, query, post)` |
| `HudChat::addLine` | `0x004fdcd0` | `ctrl.addLine(line)` |
| `HudCommandMsg::addLine` | `0x004fe440` | `commandMsgHud.addLine(line)` |
| `HudInventory::addInventory` | `0x004ffa00` | `inventoryHud.addInventory(inventoryNum, amount)` |
| `HudInventory::clearAll` | `0x004ffb30` | `inventoryHud.clearAll()` |
| `HudInventory::removeInventory` | `0x004ffa30` | `inventoryHud.removeInventory(inventoryNum)` |
| `HudInventory::setActiveInventory` | `0x004ffa50` | `inventoryHud.setActiveInventory(inventoryNum)` |
| `HudInventory::setAmount` | `0x004ffa70` | `inventoryHud.setAmount(inventoryNum, amount)` |
| `HudInventory::setBackGroundBitmap` | `0x004ffad0` | `inventoryHud.setBackGroundBitmap(bitmap);` |
| `HudInventory::setHighLightBitmap` | `0x004ffb10` | `inventoryHud.setHighLightBitmap(bitmap);` |
| `HudInventory::setInfiniteAmountBitmap` | `0x004ffaf0` | `inventoryHud.setInfiniteAmountBitmap(bitmap);` |
| `HudNavDisplay::keepClientTargetAlive` | `0x005094a0` | `obj.keepClientTargetAlive(targetObj)` |
| `HudNavDisplay::setMarkerTypeVisible` | `0x00509430` | `obj.setMarkerTypeVisible(type, bool)` |
| `HudVehicleWeapon::addWeapon` | `0x004ffa00` | `vehicleWeaponHud.addWeapon(weaponNum, amount)` |
| `HudVehicleWeapon::clearAll` | `0x004ffb30` | `vehicleWeaponHud.clearAll()` |
| `HudVehicleWeapon::removeWeapon` | `0x004ffa30` | `vehicleWeaponHud.removeWeapon(weaponNum)` |
| `HudVehicleWeapon::setActiveWeapon` | `0x004ffa50` | `vehicleWeaponHud.setActiveWeapon(weaponNum)` |
| `HudVehicleWeapon::setAmount` | `0x004ffa70` | `vehicleWeaponHud.setAmount(weaponNum, amount)` |
| `HudVehicleWeapon::setBackGroundBitmap` | `0x004ffad0` | `vehicleWeaponHud.setBackGroundBitmap(bitmap);` |
| `HudVehicleWeapon::setHighLightBitmap` | `0x004ffb10` | `vehicleWeaponHud.setHighLightBitmap(bitmap);` |
| `HudVehicleWeapon::setInfiniteAmountBitmap` | `0x004ffaf0` | `vehicleWeaponHud.setInfiniteAmountBitmap(bitmap);` |
| `HudWeapons::addWeapon` | `0x004ffa00` | `weaponsHud.addWeapon(weaponNum, AmmoAmount)` |
| `HudWeapons::clearAll` | `0x004ffb30` | `weaponsHud.clearAll()` |
| `HudWeapons::removeWeapon` | `0x004ffa30` | `weaponsHud.removeWeapon(weaponNum)` |
| `HudWeapons::setActiveWeapon` | `0x004ffa50` | `weaponsHud.setActiveWeapon(weaponNum)` |
| `HudWeapons::setAmmo` | `0x004ffa70` | `weaponsHud.setAmmo(weaponNum, ammoCount)` |
| `HudWeapons::setBackGroundBitmap` | `0x004ffad0` | `weaponsHud.setBackGroundBitmap(bitmap);` |
| `HudWeapons::setHighLightBitmap` | `0x004ffb10` | `weaponsHud.setHighLightBitmap(bitmap);` |
| `HudWeapons::setInfiniteAmmoBitmap` | `0x004ffaf0` | `weaponsHud.setInfiniteAmmoBitmap(bitmap);` |
| `InteriorInstance::activateLight` | `0x0051ff00` | `[InteriorObject].activateLight(<LightName>)` |
| `InteriorInstance::deactivateLight` | `0x0051ff30` | `[InteriorObject].deactivateLight(<LightName>)` |
| `InteriorInstance::echoTriggerableLights` | `0x0051ff60` | `[InteriorObject].echoTriggerableLights()` |
| `InteriorInstance::magicButton` | `0x0051ff90` | `[InteriorObject].magicButton()` |
| `InteriorInstance::setAlarmMode` | `0x0051feb0` | `[InteriorObject].setAlarmMode("On"|"Off")` |
| `InteriorInstance::setSkinBase` | `0x0051ffc0` | `[InteriorObject].setSkinBase(<basename>)` |
| `Item::blowup` | `0x006072b0` | `obj.blowup()` |
| `Lightning::strikeObject` | `0x00626be0` | `[LightningObject].strikeObject(id)` |
| `MessageVector::clear` | `0x004d75d0` | `[MessageVector].clear()` |
| `MessageVector::dump` | `0x004d7730` | `[MessageVector].dump(filename{, header})` |
| `MissionArea::setArea` | `0x00619be0` | `missionArea.setArea(x, y, w, h);` |
| `MissionAreaEditor::centerWorld` | `0x0046b610` | `missionAreaEditor.centerWorld();` |
| `MissionAreaEditor::setArea` | `0x0046bca0` | `missionAreaEditor.setArea(x, y, w, h);` |
| `MissionAreaEditor::updateTerrain` | `0x0046bd70` | `missionAreaEditor.updateTerrain();` |
| `NavigationGraph::dumpInfo2File` | `0x00481520` | `navGraph.dumpInfo2File();` |
| `NavigationGraph::spawnInfo` | `0x00481820` | `navGraph.spawnInfo();` |
| `PhysicalZone::activate` | `0x0068a910` | `obj.activate()` |
| `PhysicalZone::deactivate` | `0x0068a930` | `obj.deactivate()` |
| `Player::clearControlObject` | `0x005dc1e0` | `obj.clearControlObject()` |
| `Player::disableMove` | `0x005dc200` | `obj.disableMove(bool)` |
| `Player::setPilot` | `0x005dc220` | `obj.setPilot(bool)` |
| `Precipitation::setPercentage` | `0x00680ba0` | `precipitation.setPercentage(percentage <1.0 to 0.0>)` |
| `Precipitation::stormPrecipitation` | `0x00680bd0` | `precipitation.stormPrecipitation(Percentage <0 to 1>, Time<sec>)` |
| `Precipitation::stormShow` | `0x00680c10` | `precipitation.stormShow(bool)` |
| `ShapeBase::applyDamage` | `0x005f1b80` | `obj.applyDamage(value)` |
| `ShapeBase::applyRepair` | `0x005f1bb0` | `obj.applyRepair(value)` |
| `ShapeBase::blowup` | `0x005f25a0` | `obj.blowup()` |
| `ShapeBase::hide` | `0x005f0b70` | `obj.hide(bool)` |
| `ShapeBase::scopeWhenSensorVisible` | `0x005f21e0` | `obj.scopeWhenSensorVisible(bool)` |
| `ShapeBase::setCloaked` | `0x005f1d00` | `obj.setCloaked(true|false)` |
| `ShapeBase::setDamageFlash` | `0x005f1d90` | `obj.setDamageFlash(flash level)` |
| `ShapeBase::setDamageLevel` | `0x005f1ad0` | `obj.setDamageLevel(value)` |
| `ShapeBase::setDeployRotation` | `0x005f22c0` | `setDeployRotation( normal )` |
| `ShapeBase::setEnergyLevel` | `0x005f1a70` | `obj.setEnergyLevel(value)` |
| `ShapeBase::setInvincibleMode` | `0x005f1f90` | `obj.setInvincibleMode(time <sec>, speed)` |
| `ShapeBase::setJammerFX` | `0x005f2620` | `obj.setJammerFX()` |
| `ShapeBase::setMomentumVector` | `0x005f25c0` | `obj.setMomentumVector()` |
| `ShapeBase::setRechargeRate` | `0x005f1c40` | `obj.setRechargeRate(value)` |
| `ShapeBase::setRepairRate` | `0x005f1be0` | `obj.setRepairRate(value)` |
| `ShapeBase::setWhiteOut` | `0x005f1df0` | `obj.setWhiteOut(flash level)` |
| `ShapeBase::startFade` | `0x005f2500` | `startFade( U32, U32, bool )` |
| `ShapeBase::unmount` | `0x005f0e80` | `obj.unmount()` |
| `ShellDlgFrame::setTitle` | `0x004e9e80` | `dlgFrame.setTitle( newTitle );` |
| `ShellFancyArray::addColumn` | `0x004ebb90` | `array.addColumn( key, name, defaultWidth, minWidth, maxWidth{, flags} );` |
| `ShellFancyArray::addRow` | `0x004ebbf0` | `array.addRow();` |
| `ShellFancyArray::clearColumns` | `0x004ebb70` | `array.clearColumns();` |
| `ShellFancyArray::clearList` | `0x004ebc60` | `array.clearList();` |
| `ShellFancyArray::forceUpdate` | `0x004ebc40` | `array.forceUpdate();` |
| `ShellFancyArray::scrollVisible` | `0x004ebe20` | `array.scrollVisible( row )` |
| `ShellFancyArray::setNumRows` | `0x004ebc10` | `array.setNumRows( numRows );` |
| `ShellFancyArray::setSecondarySortColumn` | `0x004ebd10` | `array.setSecondarySortColumn( key );` |
| `ShellFancyArray::setSecondarySortIncreasing` | `0x004ebd30` | `array.setSecondarySortIncreasing( <bool> );` |
| `ShellFancyArray::setSelectedRow` | `0x004ebc80` | `array.setSelectedRow( row );` |
| `ShellFancyArray::setSortColumn` | `0x004ebcb0` | `array.setSortColumn( key );` |
| `ShellFancyArray::setSortIncreasing` | `0x004ebcd0` | `array.setSortIncreasing( <bool> );` |
| `ShellFancyTextList::addRow` | `0x004f1a50` | `fancytextlist.addRow( id, text{, index } )` |
| `ShellFancyTextList::clear` | `0x004f1a30` | `fancytextlist.clear()` |
| `ShellFancyTextList::clearSelection` | `0x004f1a10` | `fancytextlist.clearSelection()` |
| `ShellFancyTextList::removeRow` | `0x004f1b50` | `fancytextlist.removeRow( index )` |
| `ShellFancyTextList::removeRowById` | `0x004f1b00` | `fancytextlist.removeRowById( id )` |
| `ShellFancyTextList::setRowById` | `0x004f1ab0` | `fancytextlist.setRowById( id, text )` |
| `ShellFancyTextList::setRowStyle` | `0x004f1d80` | `fancytextlist.setRowStyle( row, style )` |
| `ShellFancyTextList::setRowStyleById` | `0x004f1db0` | `fancytextlist.setRowStyleById( id, style )` |
| `ShellFancyTextList::setSelectedById` | `0x004f19f0` | `fancytextlist.setSelectedById( id )` |
| `ShellFancyTextList::sort` | `0x004f1bd0` | `fancytextlist.sort( { column{, increasing} } )` |
| `ShellPaneCtrl::setTitle` | `0x004e91d0` | `pane.setTitle( newTitle );` |
| `ShellTabFrame::setAltColor` | `0x004f5c70` | `frame.setAltColor( <bool> );` |
| `ShellTabGroupCtrl::addSet` | `0x004f88e0` | `tabGroup.addSet( id, bitmap, fontColor, fontColorHL, fontColorSE )` |
| `ShellTabGroupCtrl::addTab` | `0x004f8660` | `tabGroup.addTab( id, text{, type{, index}} )` |
| `ShellTabGroupCtrl::clear` | `0x004f8650` | `tabGroup.clear()` |
| `ShellTabGroupCtrl::clearTabSets` | `0x004f88d0` | `tabGroup.clearTabSets()` |
| `ShellTabGroupCtrl::removeSet` | `0x004f8ab0` | `tabGroup.removeSet( index )` |
| `ShellTabGroupCtrl::removeTab` | `0x004f87a0` | `tabGroup.removeTab( id )` |
| `ShellTabGroupCtrl::removeTabByIndex` | `0x004f87c0` | `tabGroup.removeTabByIndex( index )` |
| `ShellTabGroupCtrl::setSelected` | `0x004f8810` | `tabGroup.setSelected( id )` |
| `ShellTabGroupCtrl::setSelectedByIndex` | `0x004f8830` | `tabGroup.setSelectedByIndex( id )` |
| `ShellTabGroupCtrl::setTabActive` | `0x004f8730` | `tabGroup.setTabActive( id, <bool> )` |
| `ShellTabGroupCtrl::setTabText` | `0x004f8700` | `tabGroup.setTabText( id, text )` |
| `ShellTabGroupCtrl::sort` | `0x004f88c0` | `tabGroup.sort()` |
| `SimpleNetObject::setMessage` | `0x005c4be0` | `obj.setMessage(msg)` |
| `Sky::realFog` | `0x005ab050` | `sky.realFog(0 <off> or 1 <on>, max, min, speed)` |
| `Sky::setWindVelocity` | `0x005ab110` | `sky.setWindVelocity(x, y, z)` |
| `Sky::stormCloudsShow` | `0x005ab180` | `sky.stormCloudsShow(bool)` |
| `Sky::stormFogShow` | `0x005ab1a0` | `sky.stormFogShow(bool)` |
| `StaticShape::blowup` | `0x00602f00` | `obj.blowup()` |
| `TCPObject::connect` | `0x005bd1f0` | `obj.connect(addr)` |
| `TCPObject::disconnect` | `0x005bd210` | `obj.disconnect()` |
| `TCPObject::listen` | `0x005bd1d0` | `obj.listen(port)` |
| `TCPObject::send` | `0x005bd190` | `obj.send(string, <string> ...)` |
| `Terraformer::clearRegister` | `0x00451de0` | `Terraformer.clearRegister(r)` |
| `Terraformer::fBm` | `0x00451e00` | `Terraformer.fBm(r, freq, 0.0-1.0{roughness}, detail, seed)` |
| `Terraformer::maskFBm` | `0x00451980` | `Terraformer.maskFBm(dst, freq, 0.0-1.0{roughness}, seed, "filter array", distort_factor, distort_reg)` |
| `Terraformer::preview` | `0x00451d60` | `Terraformer.preview(dst_gui, src)` |
| `Terraformer::previewScaled` | `0x00451ce0` | `Terraformer.previewScaled(dst_gui, src)` |
| `Terraformer::rigidMultiFractal` | `0x00451f50` | `Terraformer.rigidMultiFractal(r, freq, 0.0-1.0{roughness}, detail, seed)` |
| `Terraformer::setCameraPosition` | `0x004515b0` | `Terraformer.setCameraPosition(x,y {,z})` |
| `Terraformer::setShift` | `0x00451420` | `Terraformer.setShift( x, y )` |
| `Terraformer::sinus` | `0x00451ef0` | `Terraformer.sinus(r, "filter array", seed)` |
| `TerrainEditor::attachTerrain` | `0x00458a00` | `terrainEditor.attachTerrain(<terrainObj>);` |
| `TerrainEditor::buildMaterialMap` | `0x00459140` | `terrainEditor.buildMaterialMap();` |
| `TerrainEditor::clearModifiedFlags` | `0x00459890` | `terrainEditor.clearModifiedFlags();` |
| `TerrainEditor::clearSelection` | `0x00458ff0` | `terrainEditor.clearSelection();` |
| `TerrainEditor::markEmptySquares` | `0x00459310` | `terrainEditor.markEmptySquares();` |
| `TerrainEditor::mirrorTerrain` | `0x00459950` | `terrainEditor.mirrorTerrain(dest octant index);` |
| `TerrainEditor::popBaseMaterialInfo` | `0x00459ed0` | `terrainEditor.popBaseMaterialInfo();` |
| `TerrainEditor::processAction` | `0x00459010` | `terrainEditor.processAction(<action>);` |
| `TerrainEditor::pushBaseMaterialInfo` | `0x00459da0` | `terrainEditor.pushBaseMaterialInfo();` |
| `TerrainEditor::redo` | `0x00458fb0` | `terrainEditor.redo();` |
| `TerrainEditor::resetSelWeights` | `0x00458ea0` | `terrainEditor.resetSelWeights(clear);` |
| `TerrainEditor::setAction` | `0x00458da0` | `terrainEditor.setAction(action_name);` |
| `TerrainEditor::setBrushPos` | `0x00458d40` | `terrainEditor.setBrushPos(x, y);` |
| `TerrainEditor::setBrushSize` | `0x00458c60` | `terrainEditor.setBrushSize(x, y);` |
| `TerrainEditor::setBrushType` | `0x00458b50` | `terrainEditor.setBrushType(box | ellipse | ...);` |
| `TerrainEditor::setLoneBaseMaterial` | `0x00459ff0` | `terrainEditor.setLoneBaseMaterial(material list base name);` |
| `TerrainEditor::undo` | `0x00458f70` | `terrainEditor.undo();` |
| `Turret::clearTarget` | `0x00653bd0` | `[Turret].clearTarget()` |
| `Turret::setAutoFire` | `0x00653ca0` | `[Turret].setAutoFire(bool)` |
| `Turret::setCapacitorLevel` | `0x00653d30` | `[Turret].setCapacitorLevel()` |
| `Turret::setCapacitorRechargeRate` | `0x00653d60` | `[Turret].setCapacitorRechargeRate()` |
| `Turret::setSkill` | `0x00653b60` | `[Turret].setSkill(skill< 0 - 1 >)` |
| `Vehicle::blowup` | `0x0060a240` | `obj.blowup()` |
| `WaterBlock::toggleWireFrame` | `0x005b5080` | `waterBlock.toggleWireFrame()` |
| `WorldEditor::addUndoState` | `0x00464580` | `worldEditor.addUndoState();` |
| `WorldEditor::clearSelection` | `0x004641f0` | `worldEditor.clearSelection();` |
| `WorldEditor::copySelection` | `0x004643e0` | `worldEditor.copySelection();` |
| `WorldEditor::deleteSelection` | `0x004643c0` | `worldEditor.deleteSelection();` |
| `WorldEditor::dropSelection` | `0x004643a0` | `worldEditor.dropSelection();` |
| `WorldEditor::hideSelection` | `0x00464420` | `worldEditor.hideSelection(bool);` |
| `WorldEditor::lockSelection` | `0x00464440` | `worldEditor.lockSelection(bool);` |
| `WorldEditor::pasteSelection` | `0x00464400` | `worldEditor.pasteSelection();` |
| `WorldEditor::redirectConsole` | `0x00464460` | `worldEditor.redirectConsole(objID)` |
| `WorldEditor::redo` | `0x004641d0` | `worldEditor.redo();` |
| `WorldEditor::selectObject` | `0x00464210` | `worldEditor.selectObject(object);` |
| `WorldEditor::setMode` | `0x004644e0` | `worldEditor.setMode(move|rotate|scale);` |
| `WorldEditor::undo` | `0x004641b0` | `worldEditor.undo();` |
| `WorldEditor::unselectObject` | `0x00464280` | `worldEditor.unselectObject(object);` |

## Uncorroborated (19)

Recovered from the same call sites, but the usage text does not name the symbol — so
the argument split is unconfirmed. Several are plainly datablock *field* registrations
rather than console commands, which means the registrar address is shared by more than
one registration helper. Listed for completeness; do not build against these without
re-checking the specific call site.

| Symbol | Address | Trailing string |
|---|---|---|
| `EditTSCtrl` | `0x00467f50` | `renderSphere` |
| `FlyingVehicle` | `0x00611760` | `useCreateHeight` |
| `GuiAviBitmapCtrl` | `0x004de490` | `setFilename` |
| `GuiCommanderMap` | `0x0065f210` | `cameraMove` |
| `GuiScrollCtrl` | `0x004be650` | `scrollToTop` |
| `Shockwave` | `0x0068cf10` | `setInitialState` |
| `Terraformer` | `0x004513b0` | `setTerrainInfo` |
| `hudClock` | `0x004febc0` | `setTime` |
| `DebugView::clearText` | `0x0061b050` | `debugView.ClearText(<line>)` |
| `DebugView::setText` | `0x0061afc0` | `debugView.SetText(line, text [, colorF])` |
| `GuiTerrPreviewCtrl::setValue` | `0x00465fd0` | `guiTerrPreviewCtrl.getValue(t)` |
| `GuiTextListCtrl::setRowById` | `0x004c4120` | `textList.setRow(id,text)` |
| `ShapeBase::setHeat` | `0x005f1e50` | `obj.getHeat(heat [0..1])` |
| `Sky::stormClouds` | `0x005aafd0` | `sky.stormCloudsOn(0<out> or 1<in>,Time<sec>)` |
| `Sky::stormFog` | `0x005ab010` | `sky.stormFogOn(Percentage <0 to 1>, Time<sec>)` |
| `TriggerData::onEnterTrigger` | `0x0061b780` | `[TriggerData].enterTrigger(Trigger, ObjectId)` |
| `TriggerData::onLeaveTrigger` | `0x0061b800` | `[TriggerData].leaveTrigger(Trigger, ObjectId)` |
| `TriggerData::onTickTrigger` | `0x0061b890` | `[TriggerData].tickTrigger(Trigger)` |
| `WorldEditor::ignoreObjClass` | `0x004640c0` | `worldEditor.ignoreObjectClass(class_name, ...);` |
