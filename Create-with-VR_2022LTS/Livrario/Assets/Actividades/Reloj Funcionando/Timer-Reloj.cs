using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Minutero : MonoBehaviour
{
    public float velRotacion;
    public GameObject minutos;
    public GameObject horas;
    public GameObject segundos;


    void Start()
    {
        //velRotacion = Time.deltaTime;
        velRotacion = 1; //Testing MUY rapido
    }

    void Update()
    {
        segundos.transform.Rotate(velRotacion, 0, 0, Space.World);
        minutos.transform.Rotate((velRotacion / 60), 0, 0, Space.World);
        horas.transform.Rotate((velRotacion / 60 / 12), 0, 0, Space.World);
    }
}
