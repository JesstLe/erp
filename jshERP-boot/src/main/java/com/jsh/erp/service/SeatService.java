package com.jsh.erp.service;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.Seat;
import com.jsh.erp.datasource.mappers.SeatMapper;
import com.jsh.erp.utils.StringUtil;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.List;

@Service
public class SeatService {
    private Logger logger = LoggerFactory.getLogger(SeatService.class);

    @Resource
    private SeatMapper seatMapper;

    public Seat getSeat(long id) throws Exception {
        return seatMapper.selectByPrimaryKey(id);
    }

    public List<Seat> select(String name, Long depotId, int offset, int rows) throws Exception {
        return seatMapper.selectByCondition(name, depotId, offset, rows);
    }

    public Long countSeat(String name, Long depotId) throws Exception {
        return seatMapper.countsByCondition(name, depotId);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int insertSeat(JSONObject obj, HttpServletRequest request) throws Exception {
        Seat seat = JSONObject.parseObject(obj.toJSONString(), Seat.class);
        seat.setStatus(0); // Default free
        seat.setDeleteFlag("0");
        return seatMapper.insertSelective(seat);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int updateSeat(JSONObject obj, HttpServletRequest request) throws Exception {
        Seat seat = JSONObject.parseObject(obj.toJSONString(), Seat.class);
        return seatMapper.updateByPrimaryKeySelective(seat);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int deleteSeat(Long id, HttpServletRequest request) throws Exception {
        Seat seat = new Seat();
        seat.setId(id);
        seat.setDeleteFlag("1");
        return seatMapper.updateByPrimaryKeySelective(seat);
    }

    @Transactional(value = "transactionManager", rollbackFor = Exception.class)
    public int batchDeleteSeat(String ids, HttpServletRequest request) throws Exception {
        List<Long> idList = StringUtil.strToLongList(ids);
        int result = 0;
        for(Long id : idList) {
             result += deleteSeat(id, request);
        }
        return result;
    }
}
