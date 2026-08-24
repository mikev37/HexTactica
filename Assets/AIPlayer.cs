using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AIPlayer : MonoBehaviour
{
    public Player player;
    public AIMissionObjective objective;
    // Start is called before the first frame update
    void Start()
    {
        player = GetComponent<Player>();
    }
    float timer = .5f;
    float t = 0;
    // Update is called once per frame
    void Update()
    {
        if(t >= 0) {
            t -= Time.deltaTime;
            return;
        } else {
            t = timer;
        }
        if (LevelEditor.singleton.editing || LevelEditor.singleton.unit_placement)
            return;
        //if (UnitController.uc.units.Count == player.units.Count)
        //    return;
        if (GameController.gc.activePlayer.Equals(player)) {
            List<Unit> activeUnits = player.units.Where(x => x.active).ToList();
            if(activeUnits.Count > 0) {
                Unit unit = activeUnits[0];
                GameController.gc.activeUnit = unit;
                HexTile origin = unit.tile;
                if (unit.Strength > 2) {
                    AttackMove(unit,-9000);
                    if (unit.moveAndAttack || unit.tile.Equals(origin))
                        AttackAttack(unit, -9000);
                    //CalculateThreatOdds(GameController.gc.activeUnit);
                    unit.FinishActive();
                    HexGenerator.forAllTiles(x => { x.Reset(); return 0; });
                } else {
                    Retreat(unit);
                    unit.FinishActive();
                }
            }

        }
    }


    public void AttackMove(Unit unit, int threshold) {
        //Greedy
        //find all available attack sites
        CalculateThreatOdds(unit,unit.moveRange);
        List<HexTile> moves = unit.tile.CheckMovementRange(unit);
        List<HexTile> allMoves = new List<HexTile>();
        allMoves.AddRange(moves);
        allMoves = allMoves.Where(x => x.CanEndTurnHere(unit)).ToList();
        allMoves.Add(unit.tile);
        moves = moves.Where(x => x.CanEndTurnHere(unit)).ToList() ;
        moves.Add(unit.tile);

        moves.Sort((x, y) => (int)(y.aiData.attackValue - x.aiData.attackValue));//.OrderBy(o => o.aiData.attackValue);
        moves[0].textField.text = "CHOICE BY VALUE";
        moves = moves.Where(x => x.aiData.attackValue == moves[0].aiData.attackValue).ToList();
        //if there is a tie for highest value Damage, go with the one with the least enemy attack
        if(moves.Count > 1) {
            moves.Sort((x, y) => (int)(x.getThreatTo(unit) - y.getThreatTo(unit)));//.OrderBy(o => o.aiData.attackValue);
            moves[0].textField.text = "CHOICE BY COUNTER";
            moves = moves.Where(x => x.getThreatTo(unit) == moves[0].getThreatTo(unit)).ToList();
        }
        //if there is still a tie, look for a tile that has cover
        if (moves.Count > 1 && unit.Concealment > 0) {
            moves.Sort((x, y) => (int)(y.terrain.cover - x.terrain.cover));//.OrderBy(o => o.aiData.attackValue);
            moves[0].textField.text = "CHOICE BY COVER";
            moves = moves.Where(x => x.terrain.cover == moves[0].terrain.cover).ToList();
        }
        HexTile chosenTile = null;
        //if there is still a tie, choose the one that moves you closer in more turns
        if (moves.Count > 1) {
            CalculateThreatOdds(unit, 20);
            List<HexTile> movesFarther = unit.tile.CheckMovementRange(unit,unit.moveRange * 3);
            movesFarther.Sort((x, y) => (int)(y.aiData.attackValue - x.aiData.attackValue));//.OrderBy(o => o.aiData.attackValue);
            //if(moves[0].aiData.attackValue)
            HexGenerator.forAllTiles(x => { x.textField.text = "";  return 0; });
            if (movesFarther[0].aiData.attackValue > 0) {
                movesFarther[0].textField.text = "Chosen";
                moves.ForEach(x => x.textField.text = "MOVE");
                /*moves = unit.tile.CheckMovementRange(unit);
                moves = moves.Where(x => x.CanEndTurnHere(unit)).ToList();
                moves.Add(unit.tile);*/
                
                chosenTile = FindFirstParentInList(allMoves, movesFarther[0]);
            }

        } else {
            chosenTile = moves[0];
        }
        //there are no enemies within the pathfinding range of this unit
        if(chosenTile == null) {
            chosenTile = objective.evaluateTiles(unit, allMoves);
            //chosenTile = moves[(int)UnityEngine.Random.Range(0, moves.Count)];
        }

        if (chosenTile != unit.tile) {
            Unit mover = GameController.gc.activeUnit;
            mover.CompleteMove(chosenTile);
        }

    }

    private HexTile FindFirstParentInList(List<HexTile> moves, HexTile hexTile) {
        
        if (moves.Contains(hexTile))
            return hexTile;
        if (hexTile.parent == null)
            return null;
        hexTile.textField.text = "PATH";
        return (FindFirstParentInList(moves, hexTile.parent));
    }

    public void AttackAttack(Unit unit, int valueThreshold) {
        List<HexTile> moves = unit.tile.CheckAttackRange(unit);
        if (moves.Count > 0) {
            moves.Sort((x, y) => (int)(y.aiData.attackValue - x.aiData.attackValue));//.OrderBy(o => o.aiData.attackValue);
            moves[0].textField.text = "" + moves[0].aiData.attackValue;
            if (moves[0].aiData.attackValue >= valueThreshold) {
                moves[0].textField.text = "ATTACK";
                unit.OrderAttack(moves[0].occupier);
            }
        }
    }

    public void Retreat(Unit unit) {
        List<HexTile> moves = unit.tile.CheckMovementRange(unit);
        moves = moves.Where(x => x.CanEndTurnHere(unit)).ToList();
        moves.Sort((x, y) => (int)(x.getThreatTo(unit) - y.getThreatTo(unit)));//.OrderBy(o => o.aiData.attackValue);
        moves[0].textField.text = "CHOICE BY COUNTER";
        moves = moves.Where(x => x.getThreatTo(unit) == moves[0].getThreatTo(unit)).ToList();
    
        //if there is still a tie, look for a tile that has cover
        if (moves.Count > 1 && unit.Concealment > 0) {
            moves.Sort((x, y) => (int) (y.terrain.cover - x.terrain.cover));//.OrderBy(o => o.aiData.attackValue);
            moves[0].textField.text = "CHOICE BY COVER";
            moves = moves.Where(x => x.terrain.cover == moves[0].terrain.cover).ToList();
        }
        if(moves.Count > 1) {
            //moves.Where(x => {x, x.neighbors.Where(y => y.occupier == player).Count() > 0 });
        }
        if (moves.Count > 1) {
            List<Unit> enemies = UnitController.uc.units;
            //enemies.re//.RemoveRange(player.units);
            //moves

        }

    }

    public void CalculateThreatOdds(Unit unit, int distance) {
        float imminentFactor = 1; // if you can attack the same turn as moving, this is a better spot to be
        HashSet<HexTile> tiles = new HashSet<HexTile>();
        List<HexTile> moves = unit.tile.CheckMovementRange(unit, distance);
        moves.Add(unit.tile);
        moves.ForEach(x => {
            if (x == unit.tile || unit.moveAndAttack)
                imminentFactor = 1;
            else imminentFactor = .1f;
            List<HexTile> attacks = x.CheckAttackRange(unit);
            attacks.ForEach(y => {
                int attackDamage = Unit.calculateDamage(unit, y.occupier, y, 0);
                float attackValue = attackDamage * y.occupier.value() * imminentFactor;

                if (attackDamage >= y.occupier.Strength)
                    attackValue *= 1.1f;
                if (unit.AttackRange == 1 && y.occupier.retaliates) {
                    int retributionDamage = Unit.calculateDamage(y.occupier, unit, x, attackDamage);
                    attackValue -= retributionDamage * unit.value();

                }
                x.aiData.attackValue = Mathf.Max(x.aiData.attackValue,attackValue);
                x.textField.text = "" + attackValue;
            });
        });
    }
}
